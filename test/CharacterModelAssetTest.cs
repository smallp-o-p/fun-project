#nullable disable warnings
using System;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class CharacterModelAssetTest
{
  private const string ZhuYuanScene = "res://scenes/models/ZhuYuan/ZhuYuan.scn";
  private const string TriggerScene = "res://scenes/models/Trigger/Trigger.scn";
  private const double TriangleEpsilon = 1e-4;

  // The authored body-shape adjustments stored as mesh blend values (capture
  // evidence in the task workspace). They are asset data; there is no runtime
  // body-shape API.
  private const string ZhuYuanBodyMesh = "Model/rig_D/GeneralSkeleton/Pantyhose00";
  private const string TriggerBodyMesh = "Model/rig_D/GeneralSkeleton/Pantyhose";
  private const string SharedBodyMesh = "Model/rig_D/GeneralSkeleton/ZZZ_Size02_C";
  private static readonly (string Shape, double Value)[] ZhuYuanBodyShape =
  [("BreastsSize", 0.3920629024505615), ("GlutesSize", 0.3333333134651184), ("BodyTone", 0.4166666865348816)];
  private static readonly (string Shape, double Value)[] TriggerBodyShape =
  [("BreastsSize", 0.31954002380371094), ("ThighsSize", -0.3999999761581421), ("BodyTone", 0.4166666865348816)];

  // The hidden Zhu Yuan alternate meshes and the valid -1 garments of the four
  // clothing components they belong to.
  private static readonly string[] ZhuYuanAlternateMeshes =
  [
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Gloves_001",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Gloves_002",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Down_001",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Down_002",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Down_003",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_UP_OUT_001",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_UP_OUT_002",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_UP_001",
  ];
  private static readonly string[] ZhuYuanValidGarments =
  [
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Gloves",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_Down",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_UP_OUT",
    "Model/rig_D/GeneralSkeleton/ZhuYuan_UP",
  ];

  // ------------------------------------------------------------------
  // Final contract: every shipped scene instantiates the native model root
  // (FromScene's fixture.Model getter rejects any other root type), authors
  // the fixed animation tree child, the root resolves it to its actual scene
  // node, the native tree starts active with its staged zero amounts, and the
  // root's wardrobe discovers the authored clothing data.
  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene)]
  [TestCase(TriggerScene)]
  public void NativeComponentsResolveAndTheTreeStartsActive(string scene)
  {
    using var fixture = ModelFixture.FromScene(scene);

    AnimationTree tree = fixture.Model.AnimationTree;
    CharacterModel wardrobe = fixture.Model;
    Assert.True(tree.Name == "ModelAnimationTree",
      $"The '{scene}' animation tree resolved to '{tree.Name}' instead of 'ModelAnimationTree'.");

    Assert.True(tree.Active,
      $"The '{scene}' native animation tree must be active by default after the cutover.");
    Assert.True(tree.TreeRoot is not null,
      $"The '{scene}' animation tree carries no graph.");
    Assert.True(tree.AnimPlayer == "../ModelAnimationPlayer",
      $"The '{scene}' animation tree drives '{tree.AnimPlayer}' instead of the native player.");

    Assert.Equal(0.0, tree.Get("parameters/Movement/blend_amount").AsDouble(),
      $"The '{scene}' Movement blend does not start at zero.");
    Assert.Equal(0.0, tree.Get("parameters/HeadTurn/blend_amount").AsDouble(),
      $"The '{scene}' HeadTurn blend does not start at zero.");

    Assert.True(wardrobe.ClothingEnabled,
      $"The '{scene}' wardrobe initializes with clothing disabled.");
    Assert.True(wardrobe.Pieces.Count > 0, $"The '{scene}' wardrobe discovered no pieces.");
    Assert.True(wardrobe.Outfits.Count > 0, $"The '{scene}' wardrobe discovered no outfits.");
  }

  [TestCase]
  public void AnimationTreeGetterRequiresReadyConfiguredModel()
  {
    using var fixture = ModelFixture.WithAnimations(start: false);
    Assert.Throws<InvalidOperationException>(() => _ = fixture.Model.AnimationTree);
    fixture.Start();
    Assert.True(ModelFixture.SameNative(fixture.AnimationTree, fixture.Model.AnimationTree));
    using var unconfigured = new ModelFixture();
    Assert.Throws<InvalidOperationException>(() => _ = unconfigured.Model.AnimationTree);
  }

  // ------------------------------------------------------------------
  // Static materials: the authored outline settings (enabled, width 1)
  // and NextPass wiring survive per-instance isolation without any
  // runtime outline writer.
  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene)]
  [TestCase(TriggerScene)]
  public void AuthoredOutlineSettingsSurviveIsolationWithoutRuntimeWrites(string scene)
  {
    using var fixture = ModelFixture.FromScene(scene, start: false);
    var authoredIds = new SysColGeneric.List<(MeshInstance3D Mesh, int Surface, ulong Material, ulong Outline)>();
    foreach (Node node in fixture.Root.FindChildren("*", "MeshInstance3D", true, false))
    {
      var mesh = (MeshInstance3D)node;
      if (mesh.Mesh is null)
        continue;
      for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
      {
        if (mesh.GetSurfaceOverrideMaterial(surface) is Material material)
          authoredIds.Add((mesh, surface, material.GetInstanceId(), material.NextPass?.GetInstanceId() ?? 0));
      }
    }
    fixture.Start();
    foreach (var saved in authoredIds)
    {
      Material material = saved.Mesh.GetSurfaceOverrideMaterial(saved.Surface);
      Assert.Equal(saved.Material, material.GetInstanceId(), "Ready must use the scene-local material already instantiated by Godot.");
      Assert.Equal(saved.Outline, material.NextPass?.GetInstanceId() ?? 0, "Ready must preserve the scene-local outline.");
    }
    Assert.True(FindNamedDescendant(fixture.Root, "Appearance") is null,
      $"The '{scene}' still carries a leftover appearance node; the appearance lives on the model root.");
    Assert.True(FindNamedDescendant(fixture.Root, "Wardrobe") is null,
      $"The '{scene}' still carries a leftover wardrobe node; the wardrobe lives on the model root.");
    var meshes = new SysColGeneric.List<MeshInstance3D>();
    CollectDescendants(fixture.Root, meshes);
    int outlinedSurfaces = 0;
    foreach (MeshInstance3D mesh in meshes)
    {
      if (mesh.Mesh is null)
        continue;
      for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
      {
        if (mesh.GetSurfaceOverrideMaterial(surface) is not ShaderMaterial baseMaterial)
          continue;
        if (baseMaterial.NextPass is not ShaderMaterial outline)
          continue;
        outlinedSurfaces++;
        Assert.True(outline.GetShaderParameter("enabled").AsBool(),
          $"{scene} '{fixture.Root.GetPathTo(mesh)}' surface {surface} authored outline is disabled.");
        Assert.True(Math.Abs(1.0 - outline.GetShaderParameter("width_scale").AsDouble()) <= 1e-6,
          $"{scene} '{fixture.Root.GetPathTo(mesh)}' surface {surface} authored outline width drifted from 1.");
      }
    }
    Assert.True(outlinedSurfaces > 0,
      $"The '{scene}' surfaces carry no outline passes; the authored outlines were lost.");
  }

  // ------------------------------------------------------------------
  // Authored body-shape adjustments live as stored mesh blend values on
  // both body meshes of each model.
  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene)]
  [TestCase(TriggerScene)]
  public void AuthoredBodyShapeValuesAreStored(string scene)
  {
    using var fixture = ModelFixture.FromScene(scene);
    (string mesh, (string Shape, double Value)[] shapes)[] meshes = scene == ZhuYuanScene
      ? [(ZhuYuanBodyMesh, ZhuYuanBodyShape), (SharedBodyMesh, ZhuYuanBodyShape)]
      : [(TriggerBodyMesh, TriggerBodyShape), (SharedBodyMesh, TriggerBodyShape)];
    foreach ((string mesh, (string Shape, double Value)[] shapes) in meshes)
      AssertBodyShape(fixture.Root, mesh, shapes);
  }

  // ------------------------------------------------------------------
  // Authored mask defaults must still select the captured renderer state:
  // Zhu Yuan's stored default mesh (with its degenerate sentinel),
  // Trigger's full pantyhose source mesh, and Trigger's generated body
  // variant.
  // ------------------------------------------------------------------

  [TestCase]
  public void AuthoredMaskDefaultsRenderTheCapturedVariants()
  {
    using var zhu = ModelFixture.FromScene(ZhuYuanScene);
    ArrayMesh? authoredDefault = MaskSetupOf(zhu.Model, SharedBodyMesh).Configuration?.DefaultMesh;
    Assert.True(authoredDefault is not null,
      "Zhu Yuan's body mask configuration carries no stored default mesh.");
    foreach (CharacterModel.MaskRuntime.Region region in zhu.Model.ResolveMask(SharedBodyMesh).Regions)
      Assert.True(region.Enabled,
        $"Zhu Yuan's authored body default left region '{region.Name}' disabled.");
    Assert.True(ModelFixture.SameNative(zhu.Model.MaskRenderMesh(SharedBodyMesh), authoredDefault),
      "Zhu Yuan's body mask does not render its stored default mesh.");
    Assert.Equal(1566, TestData.VisibleTriangles(zhu.Model.MaskRenderMesh(SharedBodyMesh)),
      "Zhu Yuan's body mask must render the stored default mesh's 1566 visible triangles.");

    using var trigger = ModelFixture.FromScene(TriggerScene);
    foreach (CharacterModel.MaskRuntime.Region region in trigger.Model.ResolveMask(TriggerBodyMesh).Regions)
      Assert.False(region.Enabled,
        $"Trigger's authored pantyhose default left region '{region.Name}' enabled.");
    Assert.True(ModelFixture.SameNative(
      trigger.Model.MaskRenderMesh(TriggerBodyMesh), SourceMeshOf(trigger.Model, TriggerBodyMesh)),
      "Trigger's pantyhose mask does not render its full source mesh.");
    Assert.Equal(8404, TestData.VisibleTriangles(trigger.Model.MaskRenderMesh(TriggerBodyMesh)),
      "Trigger's pantyhose mask must render the full source mesh's 8404 triangles.");

    int enabledBodyRegions = 0;
    foreach (CharacterModel.MaskRuntime.Region region in trigger.Model.ResolveMask(SharedBodyMesh).Regions)
    {
      if (region.Enabled)
        enabledBodyRegions++;
    }
    Assert.True(enabledBodyRegions == 3,
      $"Trigger's authored body default enables {enabledBodyRegions} regions, not the captured 3.");
    Assert.True(!ModelFixture.SameNative(
      trigger.Model.MaskRenderMesh(SharedBodyMesh), SourceMeshOf(trigger.Model, SharedBodyMesh))
      && !ModelFixture.SameNative(trigger.Model.MaskRenderMesh(SharedBodyMesh),
        MaskSetupOf(trigger.Model, SharedBodyMesh).Configuration?.DefaultMesh),
      "Trigger's body mask must render a generated variant, not its source or default mesh.");
    Assert.Equal(6054, TestData.VisibleTriangles(trigger.Model.MaskRenderMesh(SharedBodyMesh)),
      "Trigger's body mask must render the generated variant's 6054 triangles.");
  }

  // ------------------------------------------------------------------
  // Real-scene wardrobe operations: the outfit switch reworks the
  // Trigger UpperBody piece and the body mask together, a direct mask
  // override is re-derived away by the next clothing write, and the
  // authored defaults reappear afterwards.
  // ------------------------------------------------------------------

  [TestCase]
  public void TriggerOutfitSwitchReworksPiecesAndBodyMask()
  {
    using var fixture = ModelFixture.FromScene(TriggerScene);
    CharacterModel wardrobe = fixture.Model;
    Assert.Equal(0, wardrobe.OutfitIndex,
      $"Expected Trigger's authored outfit index 0, got {wardrobe.OutfitIndex}.");
    long authoredTriangles = TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh));
    (Node originalPiece, Node switchedPiece) = TriggerUpperBodyPieces(fixture.Root, wardrobe);
    Assert.True(IsVisible(originalPiece) && !IsVisible(switchedPiece),
      "Expected Trigger's original-outfit UpperBody piece visible and the "
      + "clothing_voluptuous piece hidden before the switch.");

    wardrobe.Outfits[2].Select();
    Assert.Equal(2, wardrobe.OutfitIndex,
      $"The outfit switch left the wardrobe on variant {wardrobe.OutfitIndex} instead of 2.");
    Assert.True(!IsVisible(originalPiece) && IsVisible(switchedPiece),
      "The outfit switch did not swap the Trigger UpperBody piece visibility.");
    long switchedTriangles = TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh));
    Assert.True(switchedTriangles != authoredTriangles,
      $"The outfit switch left the body mask at {switchedTriangles} visible triangles, "
      + $"unchanged from the authored {authoredTriangles}.");

    wardrobe.Outfits[0].Select();
    Assert.Equal(0, wardrobe.OutfitIndex,
      $"Restoring the outfit left the wardrobe on variant {wardrobe.OutfitIndex} instead of 0.");
    Assert.True(IsVisible(originalPiece) && !IsVisible(switchedPiece),
      "Restoring the outfit did not swap the Trigger UpperBody piece visibility back.");
    Assert.True(TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh)) == authoredTriangles,
      $"Restoring the outfit left the body mask at "
      + $"{TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh))} visible triangles "
      + $"instead of the authored {authoredTriangles}.");

    // A direct region override diverges from the authored clothing state; the
    // next clothing derivation re-applies the authored rules.
    TestData.SelectMaskRegions(wardrobe, SharedBodyMesh);
    Assert.True(TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh)) != authoredTriangles,
      "The direct region override did not change the rendered body mask.");
    wardrobe.ClothingEnabled = false;
    wardrobe.ClothingEnabled = true;
    Assert.True(TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh)) == authoredTriangles,
      $"The clothing derivation left the body mask at "
      + $"{TestData.VisibleTriangles(wardrobe.MaskRenderMesh(SharedBodyMesh))} visible triangles "
      + $"instead of the authored {authoredTriangles}.");
  }

  // The eight alternates never join an outfit, so every clothing derivation
  // must leave them hidden while the valid -1 garments keep driving visibility.
  [TestCase]
  public void ZhuYuanAlternateGarmentsStayHiddenThroughClothingToggles()
  {
    using var fixture = ModelFixture.FromScene(ZhuYuanScene);
    CharacterModel wardrobe = fixture.Model;
    Assert.True(wardrobe.ClothingEnabled,
      $"The migrated '{ZhuYuanScene}' initializes with clothing disabled.");

    AssertGarmentVisibility(fixture.Model, ZhuYuanAlternateMeshes, false, "after initialization");
    AssertGarmentVisibility(fixture.Model, ZhuYuanValidGarments, true, "after initialization");

    wardrobe.ClothingEnabled = false;
    AssertGarmentVisibility(fixture.Model, ZhuYuanAlternateMeshes, false, "with clothing disabled");
    AssertGarmentVisibility(fixture.Model, ZhuYuanValidGarments, false, "with clothing disabled");

    wardrobe.ClothingEnabled = true;
    AssertGarmentVisibility(fixture.Model, ZhuYuanAlternateMeshes, false, "after clothing was restored");
    AssertGarmentVisibility(fixture.Model, ZhuYuanValidGarments, true, "after clothing was restored");
  }

  // ------------------------------------------------------------------
  // Instance independence: clothing derivations and preset weight writes
  // on one instance never reach another instance of the same scene.
  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene)]
  [TestCase(TriggerScene)]
  public void ClothingAndPresetWeightChangesStayIndependentAcrossInstances(string scene)
  {
    using var first = ModelFixture.FromScene(scene);
    using var second = ModelFixture.FromScene(scene);
    NodePath bodyMaskPath = second.Model.MaskSetups[0].MeshPath;
    long secondTriangles = TestData.VisibleTriangles(second.Model.MaskRenderMesh(bodyMaskPath));
    bool secondGlovesVisible = second.Piece("Gloves").IsVisible;
    string presetParameter = ModelAnimationGraph.SortedPresetParameters(
      (AnimationNodeBlendTree)first.Model.AnimationTree.TreeRoot)[0];
    double secondWeightBefore = second.Model.AnimationTree.Get(presetParameter).AsDouble();
    Assert.Equal(0.0, secondWeightBefore,
      $"The '{scene}' preset parameter '{presetParameter}' does not start at zero.");

    first.Model.ClothingEnabled = false;
    first.Model.AnimationTree.Set(presetParameter, 0.5);

    Assert.False(first.Piece("Gloves").IsVisible,
      $"Disabling clothing on the first '{scene}' instance did not hide its Gloves piece.");
    Assert.True(second.Piece("Gloves").IsVisible == secondGlovesVisible,
      $"The first '{scene}' instance's clothing change reached the second instance's Gloves piece.");
    Assert.True(TestData.VisibleTriangles(second.Model.MaskRenderMesh(bodyMaskPath)) == secondTriangles,
      $"The first '{scene}' instance's clothing change reached the second instance's body mask.");
    Assert.Equal(secondWeightBefore, second.Model.AnimationTree.Get(presetParameter).AsDouble(),
      $"The first '{scene}' instance's preset weight write reached the second instance.");
  }

  // ------------------------------------------------------------------
  // Attachment visibility on the real scenes: Zhu Yuan explicitly
  // configures her (default-hidden) Weapons container through the root
  // flag/path; Trigger keeps an empty path and tolerates the flag.
  // ------------------------------------------------------------------

  [TestCase]
  public void ZhuYuanAttachmentsStartHiddenAndToggleThroughTheRoot()
  {
    using var fixture = ModelFixture.FromScene(ZhuYuanScene);
    CharacterModel model = fixture.Model;
    Assert.False(model.AttachmentsPath.IsEmpty,
      $"The '{ZhuYuanScene}' root does not configure its attachments path.");
    var weapons = model.GetNode<Node3D>(model.AttachmentsPath);
    Assert.True(weapons.Name == "Weapons",
      $"The '{ZhuYuanScene}' attachments path resolves to '{weapons.Name}' instead of 'Weapons'.");
    Assert.True(weapons.GetScript().AsGodotObject() is null,
      $"The '{ZhuYuanScene}' Weapons container must be a plain Node3D, not a scripted group.");
    Assert.False(model.AttachmentsVisible,
      $"The '{ZhuYuanScene}' root must ship with attachments hidden.");
    Assert.False(weapons.Visible,
      $"The '{ZhuYuanScene}' Weapons container must be hidden in the saved scene.");

    var parts = new SysColGeneric.List<Node>();
    foreach (Node child in weapons.GetChildren())
      parts.Add(child);
    Assert.True(parts.Count > 0,
      $"The '{ZhuYuanScene}' Weapons container carries no children.");
    var authoredFlags = new SysColGeneric.List<bool>();
    foreach (Node part in parts)
    {
      Assert.False(IsEffectivelyVisible(part),
        $"The '{ZhuYuanScene}' child '{part.Name}' is effectively visible while its container is hidden.");
      authoredFlags.Add(IsVisible(part));
    }

    model.AttachmentsVisible = true;
    Assert.True(weapons.Visible,
      "Toggling the root flag must show the whole container.");
    for (int i = 0; i < parts.Count; i++)
    {
      Assert.True(IsVisible(parts[i]) == authoredFlags[i],
        $"Toggling visibility rewrote the authored flag of '{parts[i].Name}'.");
      Assert.True(IsEffectivelyVisible(parts[i]) == authoredFlags[i],
        $"The effective visibility of '{parts[i].Name}' must follow the container when shown.");
    }

    model.AttachmentsVisible = false;
    Assert.False(weapons.Visible);
    foreach (Node part in parts)
      Assert.False(IsEffectivelyVisible(part),
        $"Re-hiding left '{part.Name}' effectively visible.");
  }

  [TestCase]
  public void AttachmentVisibilityStaysIndependentAcrossInstances()
  {
    using var first = ModelFixture.FromScene(ZhuYuanScene);
    using var second = ModelFixture.FromScene(ZhuYuanScene);

    first.Model.AttachmentsVisible = true;
    Assert.True(first.Model.GetNode<Node3D>(first.Model.AttachmentsPath).Visible,
      "Toggling the first instance must show its container.");
    Assert.False(second.Model.AttachmentsVisible,
      "The first instance's toggle reached the second instance's flag.");
    Assert.False(second.Model.GetNode<Node3D>(second.Model.AttachmentsPath).Visible,
      "The first instance's toggle reached the second instance's container.");
  }

  [TestCase]
  public void TriggerToleratesUnconfiguredAttachments()
  {
    using var fixture = ModelFixture.FromScene(TriggerScene);
    Assert.True(fixture.Model.AttachmentsPath.IsEmpty,
      $"The '{TriggerScene}' root must keep its attachments path empty.");
    Assert.True(fixture.Model.AttachmentsVisible,
      $"The '{TriggerScene}' root keeps the general visible default.");
    fixture.Model.AttachmentsVisible = false;
    fixture.Model.AttachmentsVisible = true;
  }

  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene, "res://scenes/models/ZhuYuan/ZhuYuan.blend")]
  [TestCase(TriggerScene, "res://scenes/models/Trigger/Trigger4.2.blend")]
  public void SceneLocalMaterialsKeepMasksAndOutlinesIndependent(string scene, string importedScene)
  {
    using var first = ModelFixture.FromScene(scene, start: false);
    using var second = ModelFixture.FromScene(scene, start: false);
    SceneState source = ResourceLoader.Load<PackedScene>(importedScene).GetState();
    var surfaces = new SysColGeneric.List<(ShaderMaterial Source, ShaderMaterial First, ShaderMaterial Second,
      Variant Weights)>();
    var outlines = new SysColGeneric.HashSet<ulong>();
    for (int node = 0; node < source.GetNodeCount(); node++)
    {
      for (int property = 0; property < source.GetNodePropertyCount(node); property++)
      {
        string name = source.GetNodePropertyName(node, property);
        const string prefix = "surface_material_override/";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
          || source.GetNodePropertyValue(node, property).AsGodotObject() is not ShaderMaterial authored)
          continue;
        int surface = int.Parse(name[prefix.Length..]);
        NodePath path = "Model/" + source.GetNodePath(node);
        var a = (ShaderMaterial)first.Root.GetNode<MeshInstance3D>(path).GetSurfaceOverrideMaterial(surface);
        var b = (ShaderMaterial)second.Root.GetNode<MeshInstance3D>(path).GetSurfaceOverrideMaterial(surface);
        AssertMaterialLocality(authored, a, b);
        if (authored.NextPass is ShaderMaterial outline)
        {
          AssertMaterialLocality(outline, (ShaderMaterial)a.NextPass, (ShaderMaterial)b.NextPass);
          Assert.True(outlines.Add(a.NextPass.GetInstanceId()), "Independently masked surfaces must not alias an outline pass.");
        }
        surfaces.Add((authored, a, b,
          (authored.NextPass as ShaderMaterial)?.GetShaderParameter("vertex_weights") ?? default));
      }
    }
    Assert.True(surfaces.Count > 0, "The imported scene must expose authored surface overrides.");
    first.Start();
    second.Start();
    foreach (ModelMeshMaskSetup setup in first.Model.MaskSetups)
    {
      CharacterModel.MaskRuntime a = first.Model.ResolveMask(setup.MeshPath);
      CharacterModel.MaskRuntime b = second.Model.ResolveMask(setup.MeshPath);
      a.PrepareSelection(a.Regions, a.Regions)();
      b.PrepareSelection(b.Regions, [])();
      Assert.True(TestData.VisibleTriangles(first.Model.MaskRenderMesh(setup.MeshPath))
        < TestData.VisibleTriangles(second.Model.MaskRenderMesh(setup.MeshPath)),
        "Different mask selections must retain independent rendered geometry.");
    }
    int outlinedSurfaces = 0;
    foreach (var item in surfaces)
    {
      if (item.Source.NextPass is not ShaderMaterial sourceOutline)
        continue;
      Assert.True(SameTextureParameter(item.Weights, sourceOutline.GetShaderParameter("vertex_weights")),
        "Masking must not replace the authored source texture.");
      var firstOutline = (ShaderMaterial)item.First.NextPass;
      var secondOutline = (ShaderMaterial)item.Second.NextPass;
      outlinedSurfaces++;
      double width = sourceOutline.GetShaderParameter("width_scale").AsDouble();
      double otherWidth = secondOutline.GetShaderParameter("width_scale").AsDouble();
      firstOutline.SetShaderParameter("width_scale", width + 1.0);
      Assert.Equal(width, sourceOutline.GetShaderParameter("width_scale").AsDouble());
      Assert.Equal(otherWidth, secondOutline.GetShaderParameter("width_scale").AsDouble());
    }
    Assert.True(outlinedSurfaces > 0, "Mutable outlines must be exercised.");
  }

  // Resource property reads can create new managed wrappers for the same native texture.
  private static bool SameTextureParameter(Variant first, Variant second)
    => first.VariantType == second.VariantType && (first.VariantType != Variant.Type.Object
      ? first.Equals(second) : ModelFixture.SameNative(first.AsGodotObject(), second.AsGodotObject()));

  private static void AssertMaterialLocality(ShaderMaterial source, ShaderMaterial first, ShaderMaterial second)
  {
    Assert.True(source.ResourceLocalToScene);
    Assert.False(ModelFixture.SameNative(source, first));
    Assert.False(ModelFixture.SameNative(first, second));
    Assert.True(ModelFixture.SameNative(source.Shader, first.Shader));
    Assert.True(ModelFixture.SameNative(source.Shader, second.Shader));
    Assert.False(source.Shader.ResourceLocalToScene);
    foreach (Godot.Collections.Dictionary uniform in source.Shader.GetShaderUniformList())
    {
      StringName name = uniform["name"].AsStringName();
      Variant value = source.GetShaderParameter(name);
      if (value.VariantType != Variant.Type.Object || value.AsGodotObject() is not Texture2D texture)
        continue;
      Assert.False(texture.ResourceLocalToScene);
      Assert.True(ModelFixture.SameNative(texture, first.GetShaderParameter(name).AsGodotObject()));
      Assert.True(ModelFixture.SameNative(texture, second.GetShaderParameter(name).AsGodotObject()));
    }
  }

  private static string DescribeRoot(Node root)
  {
    string script = (root.GetScript().AsGodotObject() as Script)?.ResourcePath ?? "";
    return script.Length > 0 ? $"{root.GetClass()} with script '{script}'" : root.GetClass();
  }

  private static void AssertBodyShape(Node root, string meshPath, (string Shape, double Value)[] expected)
  {
    var mesh = root.GetNode<MeshInstance3D>(meshPath);
    foreach ((string shape, double value) in expected)
    {
      int index = mesh.FindBlendShapeByName(shape);
      Assert.True(index >= 0, $"The authored body mesh '{meshPath}' has no blend shape '{shape}'.");
      float actual = mesh.GetBlendShapeValue(index);
      Assert.True(Math.Abs(actual - value) <= TriangleEpsilon,
        $"The authored body shape '{shape}' on '{meshPath}' reads {actual} instead of {value}.");
    }
  }

  // Pieces resolve as plain Node in the wardrobe; visibility goes through the
  // generic property.
  private static bool IsVisible(Node node) => node.Get("visible").AsBool();

  // Effective visibility for generically collected nodes: a false authored
  // "visible" anywhere up the ancestor chain hides the node; ancestors
  // without the property (plain Node) are skipped.
  private static bool IsEffectivelyVisible(Node node)
  {
    for (Node current = node; current is not null; current = current.GetParentOrNull<Node>())
    {
      Variant visible = current.Get("visible");
      if (visible.VariantType != Variant.Type.Nil && !visible.AsBool())
        return false;
    }
    return true;
  }

  private static void AssertGarmentVisibility(Node root, string[] paths, bool expected, string when)
  {
    foreach (string path in paths)
    {
      Node node = root.GetNode(path);
      Assert.True(IsVisible(node) == expected,
        $"The Zhu Yuan garment '{path}' was {(IsVisible(node) ? "visible" : "hidden")} {when}.");
    }
  }

  private static (Node originalPiece, Node switchedPiece) TriggerUpperBodyPieces(
    Node root, CharacterModel wardrobe)
  {
    var pieces = wardrobe.WardrobeConfiguration.Components["UpperBody"].Pieces;
    Node variant0 = null;
    Node variant2 = null;
    foreach (ModelWardrobeGarment piece in pieces)
    {
      switch (piece.Variant)
      {
        case 0:
          variant0 = root.GetNode<Node>(piece.Path);
          break;
        case 2:
          variant2 = root.GetNode<Node>(piece.Path);
          break;
      }
    }
    Assert.True(variant0 is not null && variant2 is not null,
      $"Trigger's authored UpperBody pieces must cover variants 0 and 2, found {pieces.Count} pieces.");
    return (variant0, variant2);
  }

  private static Node? FindNamedDescendant(Node root, string name)
  {
    foreach (Node child in root.GetChildren())
    {
      if (child.Name == name)
        return child;
      Node? descendant = FindNamedDescendant(child, name);
      if (descendant is not null)
        return descendant;
    }
    return null;
  }

  private static void CollectDescendants<T>(Node root, SysColGeneric.List<T> results) where T : Node
  {
    foreach (Node child in root.GetChildren())
    {
      if (child is T match)
        results.Add(match);
      CollectDescendants(child, results);
    }
  }

  private static ModelMeshMaskSetup MaskSetupOf(CharacterModel model, NodePath meshPath)
  {
    foreach (ModelMeshMaskSetup setup in model.MaskSetups)
    {
      if (setup.MeshPath == meshPath)
        return setup;
    }

    throw new InvalidOperationException($"The model '{model.Name}' has no mask setup for '{meshPath}'.");
  }

  private static ArrayMesh? SourceMeshOf(CharacterModel model, NodePath meshPath)
    => model.GetNode<MeshInstance3D>(meshPath).Mesh as ArrayMesh;
}
