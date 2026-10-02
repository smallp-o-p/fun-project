using System;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class CharacterModelAppearanceAttachmentTest
{
  // ------------------------------------------------------------------
  // Per-instance material isolation
  // ------------------------------------------------------------------

  [TestCase]
  public void IsolationLeavesAuthoredMaterialsUntouchedWithoutRuntimeProcessing()
  {
    using var fixture = ModelFixture.WithAppearance();
    var material = (ShaderMaterial)fixture.Body.GetSurfaceOverrideMaterial(0)!;
    Assert.False(ModelFixture.SameNative(material, fixture.SharedMaterial));
    Assert.False(ModelFixture.SameNative(fixture.Outline, fixture.SharedMaterial!.NextPass));
    fixture.Outline.SetShaderParameter("width_scale", 2.5);
    Assert.Equal(1.0, ((ShaderMaterial)fixture.SharedMaterial.NextPass!)
      .GetShaderParameter("width_scale").AsDouble());
    Assert.False(fixture.Model.IsProcessing());
  }

  // Isolation carries authored outline settings per instance: a non-default
  // width, a disabled pass, and a surface authored without any pass survive
  // as authored.
  [TestCase]
  public void IsolationCarriesAuthoredOutlineSettings()
  {
    using var widthFixture = ModelFixture.WithAppearance(start: false,
      authorMaterial: material => ((ShaderMaterial)material.NextPass!).SetShaderParameter("width_scale", 2.5));
    widthFixture.Start();
    Assert.Equal(2.5, widthFixture.Outline.GetShaderParameter("width_scale").AsDouble());
    Assert.Equal(2.5, ((ShaderMaterial)widthFixture.SharedMaterial!.NextPass!)
      .GetShaderParameter("width_scale").AsDouble());

    using var disabledFixture = ModelFixture.WithAppearance(start: false,
      authorMaterial: material => ((ShaderMaterial)material.NextPass!).SetShaderParameter("enabled", false));
    disabledFixture.Start();
    Assert.False(disabledFixture.Outline.GetShaderParameter("enabled").AsBool());
    Assert.False(((ShaderMaterial)disabledFixture.SharedMaterial!.NextPass!)
      .GetShaderParameter("enabled").AsBool());

    using var passlessFixture = ModelFixture.WithAppearance(start: false,
      authorMaterial: material => material.NextPass = null);
    passlessFixture.Start();
    Assert.That(passlessFixture.Body.GetSurfaceOverrideMaterial(0)!.NextPass is null,
      "A surface authored without an outline pass must stay without one after isolation.");
    Assert.That(passlessFixture.SharedMaterial!.NextPass is null);
  }

  [TestCase]
  public void InitializeRetryReusesIsolatedMaterials()
  {
    // Failed initialization and its late-assignment retry must keep the same
    // native resources that PackedScene already localized.
    using var fixture = ModelFixture.WithAppearance(withMask: true, start: false);
    ModelMeshMaskSetup setup = fixture.Model.MaskSetups[0];
    NodePath meshPath = setup.MeshPath;
    setup.MeshPath = "MissingBody";
    fixture.Start();
    ulong surfaceId = fixture.Body.GetSurfaceOverrideMaterial(0)!.GetInstanceId();
    ulong outlineId = fixture.Outline.GetInstanceId();

    setup.MeshPath = meshPath;
    fixture.Model.MaskSetups = new Godot.Collections.Array<ModelMeshMaskSetup> { setup };
    Assert.Equal(surfaceId, fixture.Body.GetSurfaceOverrideMaterial(0)!.GetInstanceId());
    Assert.Equal(outlineId, fixture.Outline.GetInstanceId());
    Assert.False(fixture.Model.IsProcessing());
  }

  // ------------------------------------------------------------------
  // Mask rendering against isolated materials
  // ------------------------------------------------------------------

  [TestCase]
  public void MaskRenderUsesIsolatedOutlineWeights()
  {
    // The first mask render uses overrides already localized by instantiation,
    // even though the masked body lives inside a nested mesh wrapper.
    using var fixture = ModelFixture.WithAppearance(withMask: true);
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
    Assert.True(fixture.Outline.GetShaderParameter("vertex_weights").VariantType
      == Variant.Type.Object);
    Assert.False(((ShaderMaterial)fixture.SharedMaterial!.NextPass!)
      .GetShaderParameter("vertex_weights").VariantType == Variant.Type.Object);
  }

  // ------------------------------------------------------------------
  // Attachment path authoring errors (empty paths stay valid — covered on
  // the real Trigger scene).
  // ------------------------------------------------------------------

  // Nonempty attachment paths are authoring-checked at initialization: a path
  // that misses rejects, as does one resolving to a non-container. (Empty paths
  // stay valid — covered on the real Trigger scene.)
  [TestCase]
  public void AttachmentsPathAuthoringErrors()
  {
    using var missing = new ModelFixture(start: false);
    missing.Model.AttachmentsPath = "../Missing";
    Assert.Throws<InvalidOperationException>(() => missing.Model.Initialize());

    using var wrongType = new ModelFixture(start: false);
    wrongType.Model.AddChild(new Node { Name = "NotAContainer" });
    wrongType.Model.AttachmentsPath = "NotAContainer";
    Assert.Throws<InvalidOperationException>(() => wrongType.Model.Initialize());
  }
}
