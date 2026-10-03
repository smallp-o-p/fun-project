using System;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class ModelMaskWardrobeTest
{
  // ------------------------------------------------------------------
  // Wardrobe coordination
  // ------------------------------------------------------------------

  [TestCase]
  public void OutfitIndexAccessorCoercesAndClampsStoredSelections()
  {
    // JSON import stores numeric selections as floats; the typed OutfitIndex
    // accessor must observe the stored float as its declared variant index.
    using var fixture = ModelFixture.WithWardrobe(start: false);
    fixture.Model.Selections[CharacterModel.OutfitKey] = Variant.From(1.0);
    Assert.Equal(1, fixture.Model.OutfitIndex);
    fixture.Start();
    Assert.Equal(1, fixture.Model.OutfitIndex);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.True(fixture.GarmentB!.Visible);

    // Runtime writes clamp to the authored variant range.
    fixture.Model.OutfitIndex = 7;
    Assert.Equal(1, fixture.Model.OutfitIndex);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
  }

  [TestCase]
  public void UnknownPieceComponentIsRejected()
  {
    using var fixture = ModelFixture.WithWardrobe();
    Assert.Throws<InvalidOperationException>(() => fixture.Model.SetPiece("Bogus", false));
    Assert.True(fixture.GarmentA!.Visible);
  }

  [TestCase]
  public void WardrobeChangeRetainsUnrelatedStoredBits()
  {
    // Three configured regions but the wardrobe rules only drive Mask0 and
    // Mask1, so Mask2 belongs to no rule: the derivation seeds its prepared
    // selection from the stored states minus the controlled regions, so the
    // unrelated state survives every wardrobe write.
    using var fixture = ModelFixture.WithWardrobe(maskCount: 3);
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask0", "Mask1", "Mask2");
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0", "Mask1", "Mask2");

    fixture.Model.OutfitIndex = 1;
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask1", "Mask2");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
  }

  [TestCase]
  public void OutfitWriteInsideTreeBeforeReadyIsRecordedAndAppliedOnReady()
  {
    // Godot C# hot-reload restores serialized selections (OutfitIndex.set →
    // WriteSelection) while the root already sits in the tree but before its
    // _Ready ran: the derivation must wait for Initialize instead of applying
    // against the not-yet-initialized masks.
    using var fixture = ModelFixture.WithWardrobe(start: false);
    Exception? preReadyFailure = null;
    fixture.Model.TreeEntered += () =>
    {
      try
      {
        fixture.Model.OutfitIndex = 1;
      }
      catch (Exception failure)
      {
        preReadyFailure = failure;
      }
    };

    fixture.Start();

    Assert.True(preReadyFailure is null, $"The pre-Ready write failed: {preReadyFailure}");
    Assert.Equal(1, fixture.Model.OutfitIndex);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.True(fixture.GarmentB!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
  }

  // ------------------------------------------------------------------
  // Late recovery and live Selections edits
  // ------------------------------------------------------------------

  [TestCase]
  public void SelectionsReplacementsApplyByLifecycle()
  {
    using var attached = ModelFixture.WithWardrobe();
    attached.Model.Selections = new Godot.Collections.Dictionary<string, Variant>
    {
      [CharacterModel.OutfitKey] = Variant.From(1),
      [CharacterModel.ClothingEnabledKey] = Variant.From(true),
    };
    Assert.False(attached.GarmentA!.Visible);
    Assert.True(attached.GarmentB!.Visible);
    Assert.MaskRegions(attached.Model, attached.MaskPath, "Mask1");
    Assert.Equal(1, attached.Model.OutfitIndex);
    Assert.True(attached.Model.Selections.ContainsKey(CharacterModel.PiecesPrefix + "Garment"));

    using var preInit = ModelFixture.WithWardrobe(start: false);
    preInit.Model.Selections = new Godot.Collections.Dictionary<string, Variant>
    {
      [CharacterModel.OutfitKey] = Variant.From(1),
    };
    // Stored only: no derivation and no materialization before initialization.
    Assert.False(preInit.Model.Selections.ContainsKey(CharacterModel.PiecesPrefix + "Garment"));
    Assert.True(preInit.GarmentB!.Visible);
    preInit.Start();
    Assert.True(preInit.Model.Selections.ContainsKey(CharacterModel.PiecesPrefix + "Garment"));
    Assert.False(preInit.GarmentA!.Visible);
    Assert.True(preInit.GarmentB!.Visible);
    Assert.MaskRegions(preInit.Model, preInit.MaskPath, "Mask1");

    // A replacement whose derivation fails (its garment node vanished) reverts
    // the whole assignment: the previously stored dictionary instance — not a
    // copy — survives with its selections, and garments, masks, and render stay
    // at the pre-failure state.
    using var rollback = ModelFixture.WithWardrobe();
    rollback.Model.Selections = new Godot.Collections.Dictionary<string, Variant>
    {
      [CharacterModel.OutfitKey] = Variant.From(1),
    };
    ArrayMesh renderBefore = rollback.Model.MaskRenderMesh(rollback.MaskPath)!;
    var stored = rollback.Model.Selections;
    rollback.GarmentB!.Free();
    Assert.Throws<InvalidOperationException>(() => rollback.Model.Selections =
      new Godot.Collections.Dictionary<string, Variant>
      {
        [CharacterModel.OutfitKey] = Variant.From(0),
      });
    Assert.True(ReferenceEquals(rollback.Model.Selections, stored),
      "The rejected replacement must restore the previously stored dictionary instance.");
    Assert.Equal(1, rollback.Model.Selections[CharacterModel.OutfitKey].AsInt32());
    Assert.False(rollback.GarmentA!.Visible);
    Assert.MaskRegions(rollback.Model, rollback.MaskPath, "Mask1");
    Assert.True(ModelFixture.SameNative(rollback.Model.MaskRenderMesh(rollback.MaskPath), renderBefore));
  }

  // Initialization applies the authored defaults (garments, mask derivation,
  // prebuilt default mesh, OutfitIndex fallback) and materializes one stored
  // pieces/<name> selection per component from its authored default — never
  // outfit/clothing_enabled, and never an already-stored value.
  [TestCase]
  public void InitializeMaterializesDefaultPieceSelectionsWithoutOverwrite()
  {
    using var fixture = ModelFixture.WithWardrobe();
    Assert.True(fixture.GarmentA!.Visible);
    Assert.False(fixture.GarmentB!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.True(ModelFixture.SameNative(
      fixture.Model.MaskRenderMesh(fixture.MaskPath), fixture.MaskConfiguration!.DefaultMesh));
    Assert.Equal(0, fixture.Model.OutfitIndex);
    Assert.True(fixture.Model.Selections.ContainsKey(CharacterModel.PiecesPrefix + "Garment"));
    Assert.True(fixture.Model.Selections[CharacterModel.PiecesPrefix + "Garment"].AsBool());
    Assert.False(fixture.Model.Selections.ContainsKey(CharacterModel.OutfitKey));
    Assert.False(fixture.Model.Selections.ContainsKey(CharacterModel.ClothingEnabledKey));

    using var overridden = ModelFixture.WithWardrobe(start: false);
    overridden.Model.Selections[CharacterModel.PiecesPrefix + "Garment"] = Variant.From(false);
    overridden.Start();
    Assert.False(overridden.Model.Selections[CharacterModel.PiecesPrefix + "Garment"].AsBool());
    Assert.False(overridden.GarmentA!.Visible);
    Assert.MaskRegions(overridden.Model, overridden.MaskPath);
    Assert.Equal(2, TestData.VisibleTriangles(overridden.Model.MaskRenderMesh(overridden.MaskPath)));
  }

  // ------------------------------------------------------------------
  // Mask render identities and geometry
  // ------------------------------------------------------------------

  // No mask bits render the untouched source mesh, the default bits reuse the
  // prebuilt default mesh, and any other selection builds a compacted generated
  // variant recording the surviving source vertex indices per surface.
  [TestCase]
  public void RegionSelectionsRenderSourceDefaultAndGeneratedVariants()
  {
    using var fixture = ModelFixture.WithWardrobe();
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath);
    Assert.True(ModelFixture.SameNative(
      fixture.Model.MaskRenderMesh(fixture.MaskPath), fixture.SourceMesh));
    Assert.Equal(2, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.True(ModelFixture.SameNative(
      fixture.Model.MaskRenderMesh(fixture.MaskPath), fixture.MaskConfiguration!.DefaultMesh));
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    ArrayMesh render = fixture.Model.MaskRenderMesh(fixture.MaskPath)!;
    Assert.False(ReferenceEquals(render, fixture.SourceMesh));
    Assert.Equal(1, TestData.VisibleTriangles(render));
    int[] sourceVertices = render.GetMeta("source_vertex_indices").AsGodotArray()[0].AsInt32Array();
    Assert.Equal(3, sourceVertices.Length);
    for (int i = 0; i < sourceVertices.Length; i++)
      Assert.Equal(i, sourceVertices[i]);
  }

  [TestCase]
  public void EveryRegionCombinationRendersWithoutExpectedTriangleMetadata()
  {
    // The removed expected-triangle metadata used to gate region combinations
    // to an authored allowlist; every subset now selects and renders straight
    // from the geometry.
    ArrayMesh source = TestData.MakeMaskedSourceMesh(3);
    using var fixture = ModelFixture.WithMask(source, TestData.MakeModelMaskConfiguration(source, 3));
    for (int selection = 0; selection < 1 << 3; selection++)
    {
      var regions = new SysColGeneric.List<string>();
      for (int bit = 0; bit < 3; bit++)
      {
        if ((selection & (1 << bit)) != 0)
          regions.Add($"Mask{bit}");
      }

      TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, [.. regions]);
      Assert.Equal(3 - regions.Count,
        TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)),
        $"Regions [{string.Join(", ", regions)}] did not render {3 - regions.Count} triangles.");
    }
  }

  // Mask i hides triangle i (the fixture convention); hiding every triangle
  // collapses the surface to the all-hidden convention's degenerate sentinel.
  [TestCase]
  public void AllHiddenTrianglesCollapseToTheDegenerateSentinel()
  {
    ArrayMesh source = TestData.MakeMaskedSourceMesh();
    ArrayMesh render = ModelMaskGeometry.BuildMesh(source, [[1L, 2L]], 3);
    Godot.Collections.Array arrays = render.SurfaceGetArrays(0);
    int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
    Assert.Equal(3, indices.Length);
    Assert.Equal(0, indices[0]);
    Assert.Equal(0, indices[1]);
    Assert.Equal(0, indices[2]);
    Assert.Equal(1, arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length);
    Assert.Equal(0, TestData.VisibleTriangles(render));
  }

  // Mask i hides triangle i, so hiddenBits 1 keeps triangle 1's source vertices
  // and hiddenBits 2 keeps triangle 0's. The pure geometry builder compacts
  // every per-vertex channel with its stride, carries blend shapes through the
  // same vertex mapping, and renumbers indices to the compacted order.
  [TestCase(1, 3)]
  [TestCase(2, 0)]
  public void BlendShapeAndSkinChannelsCompactWithTheirStride(int hiddenBits, int firstKeptVertex)
  {
    ArrayMesh source = TestData.MakeSkinSourceMesh();
    ArrayMesh render = ModelMaskGeometry.BuildMesh(source, [[1L, 2L]], hiddenBits);
    int[] kept = render.GetMeta("source_vertex_indices").AsGodotArray()[0].AsInt32Array();
    Assert.Equal(3, kept.Length);
    for (int i = 0; i < kept.Length; i++)
      Assert.Equal(firstKeptVertex + i, kept[i]);
    int[] indices = render.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Index].AsInt32Array();
    Assert.Equal(3, indices.Length);
    for (int i = 0; i < indices.Length; i++)
      Assert.Equal(i, indices[i]);

    // Blend shapes survive with the same mapping.
    Assert.Equal(source.GetBlendShapeCount(), render.GetBlendShapeCount());
    Assert.Equal("Wide", render.GetBlendShapeName(0).ToString());
    Godot.Collections.Array<Godot.Collections.Array> sourceShapes = source.SurfaceGetBlendShapeArrays(0);
    Godot.Collections.Array<Godot.Collections.Array> renderShapes = render.SurfaceGetBlendShapeArrays(0);
    Assert.Equal(sourceShapes.Count, renderShapes.Count);
    var sourceShapeVertices = sourceShapes[0][(int)Mesh.ArrayType.Vertex].AsVector3Array();
    var renderShapeVertices = renderShapes[0][(int)Mesh.ArrayType.Vertex].AsVector3Array();
    Assert.Equal(kept.Length, renderShapeVertices.Length);
    for (int i = 0; i < kept.Length; i++)
      Assert.Equal(sourceShapeVertices[kept[i]], renderShapeVertices[i]);

    // Vertices, colors, bones, and weights compact per source vertex with their stride.
    Godot.Collections.Array sourceArrays = source.SurfaceGetArrays(0);
    Godot.Collections.Array renderArrays = render.SurfaceGetArrays(0);
    var sourceVertices = sourceArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
    var renderVertices = renderArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
    var sourceColors = sourceArrays[(int)Mesh.ArrayType.Color].AsColorArray();
    var renderColors = renderArrays[(int)Mesh.ArrayType.Color].AsColorArray();
    var sourceBones = sourceArrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
    var renderBones = renderArrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
    var sourceWeights = sourceArrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
    var renderWeights = renderArrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
    int stride = sourceBones.Length / 6;
    Assert.Equal(kept.Length, renderVertices.Length);
    Assert.Equal(kept.Length, renderColors.Length);
    Assert.Equal(kept.Length * stride, renderBones.Length);
    Assert.Equal(kept.Length * stride, renderWeights.Length);
    for (int i = 0; i < kept.Length; i++)
    {
      int sourceVertex = kept[i];
      Assert.Equal(sourceVertices[sourceVertex], renderVertices[i]);
      Assert.Equal(sourceColors[sourceVertex], renderColors[i]);
      for (int k = 0; k < stride; k++)
      {
        Assert.Equal(sourceBones[sourceVertex * stride + k], renderBones[i * stride + k]);
        Assert.Equal(sourceWeights[sourceVertex * stride + k], renderWeights[i * stride + k]);
      }
    }

    Assert.Equal(source.SurfaceGetFormat(0), render.SurfaceGetFormat(0));
  }

  [TestCase]
  public void EightBoneWeightStrideIsPreserved()
  {
    ArrayMesh source = TestData.MakeSkinSourceMesh(eightBoneWeights: true, withBlendShape: false);
    ArrayMesh render = ModelMaskGeometry.BuildMesh(source, [[1L, 2L]], 2);
    Godot.Collections.Array renderArrays = render.SurfaceGetArrays(0);
    Assert.Equal(3 * 8, renderArrays[(int)Mesh.ArrayType.Bones].AsInt32Array().Length);
    Assert.Equal(3 * 8, renderArrays[(int)Mesh.ArrayType.Weights].AsFloat32Array().Length);
    Assert.Equal(source.SurfaceGetFormat(0), render.SurfaceGetFormat(0));
  }

  // ------------------------------------------------------------------
  // Outline weights
  // ------------------------------------------------------------------

  [TestCase]
  public void OutlineWeightsRebuildFromSourceVertexIndices()
  {
    using var fixture = ModelFixture.WithWardrobe();
    var outline = new ShaderMaterial();
    outline.SetMeta("source_weights", new float[] { 10f, 11f, 12f, 13f, 14f, 15f });
    fixture.Body!.SetSurfaceOverrideMaterial(0, new ShaderMaterial { NextPass = outline });

    // Triangle 0 survives Mask1, so the texture carries source vertices 0..2.
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    AssertFirstFloats(outline, 10f, 11f, 12f);

    // Unmasked output restores the full source weight list.
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath);
    AssertFirstFloats(outline, 10f, 11f, 12f, 13f, 14f, 15f);
  }

  [TestCase]
  public void SurfaceMaterialWithoutOutlinePassStillMasks()
  {
    // Real scenes carry materials without an outline NextPass; they must not
    // interfere with masking (outlined materials keep rebuilding their weights,
    // covered by OutlineWeightsRebuildFromSourceVertexIndices).
    using var fixture = ModelFixture.WithWardrobe();
    fixture.Body!.SetSurfaceOverrideMaterial(0, new StandardMaterial3D());
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
    Assert.False(ModelFixture.SameNative(
      fixture.Model.MaskRenderMesh(fixture.MaskPath), fixture.SourceMesh));
  }

  private static void AssertFirstFloats(ShaderMaterial outline, params float[] expected)
  {
    var texture = outline.GetShaderParameter("vertex_weights").As<ImageTexture>();
    Assert.True(texture is not null);
    Godot.Image image = texture!.GetImage();
    Assert.Equal(Godot.Image.Format.Rf, image.GetFormat());
    byte[] data = image.GetData();
    // Original convention: values pad up to at least one 256-float row.
    Assert.Equal(256, data.Length / sizeof(float));
    for (int i = 0; i < expected.Length; i++)
      Assert.Equal(expected[i], BitConverter.ToSingle(data, i * sizeof(float)));
  }

  // ------------------------------------------------------------------
  // Rejections
  // ------------------------------------------------------------------

  [TestCase]
  public void AllSixtyFourEnabledRegionsRenderOnFirstApply()
  {
    // The all-64-enabled bitset is -1, the exact value a never-rendered
    // sentinel must not collide with: the initial render must still happen,
    // and zero and all selections must render the actual geometry.
    ArrayMesh source = TestData.MakeMaskedSourceMesh(64);
    ModelMaskConfiguration configuration = TestData.MakeModelMaskConfiguration(source, 64);
    for (int i = 0; i < 64; i++)
      configuration.Masks[$"Mask{i}"] = true;
    using var fixture = ModelFixture.WithMask(source, configuration);

    Assert.Equal(0, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath);
    Assert.True(ModelFixture.SameNative(
      fixture.Model.MaskRenderMesh(fixture.MaskPath), fixture.SourceMesh));
    Assert.Equal(64, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    var all = new string[64];
    for (int i = 0; i < 64; i++)
      all[i] = $"Mask{i}";
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, all);
    Assert.Equal(0, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
  }

  [TestCase]
  public void ImportedMaskGeometryRejectsCorruptionBeforeRuntimeBinding()
  {
    ArrayMesh source = TestData.MakeMaskedSourceMesh();
    ModelMaskConfiguration configuration = TestData.MakeModelMaskConfiguration(source);
    Assert.Equal("", new ModelImportValidation().ValidateMaskGeometry(configuration, source));
    configuration.GeometryHash = "stale";
    Assert.True(new ModelImportValidation().ValidateMaskGeometry(configuration, source).Length > 0);
    configuration.GeometryHash = ModelImportValidation.GeometryHash(source);
    long[] flags = configuration.TriangleMasks[0];
    configuration.TriangleMasks[0] = flags[1..];
    Assert.True(new ModelImportValidation().ValidateMaskGeometry(configuration, source).Length > 0);
    configuration.TriangleMasks[0] = flags;
    Assert.Equal("", new ModelImportValidation().ValidateMaskGeometry(configuration, source));
    using var fixture = ModelFixture.WithMask(source, configuration);
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
  }

  [TestCase]
  public void WardrobeUsesBoundInstanceNodesAfterInitialization()
  {
    using var fixture = ModelFixture.WithWardrobe();
    fixture.GarmentA!.Name = "RenamedGarment";
    fixture.Model.ClothingEnabled = false;
    Assert.False(fixture.GarmentA.Visible);
    fixture.Model.ClothingEnabled = true;
    Assert.True(fixture.GarmentA.Visible);
  }

  [TestCase]
  public void TwoEmptyResourcePathConfigurationsDoNotCollide()
  {
    // Configurations without a resource path key their caches by instance
    // identity, so identical synthetic setups stay independent — for stored
    // default meshes and generated variants alike.
    ArrayMesh source = TestData.MakeMaskedSourceMesh();
    ModelMaskConfiguration firstConfiguration = TestData.MakeModelMaskConfiguration(source, defaultBits: 1);
    ModelMaskConfiguration secondConfiguration = TestData.MakeModelMaskConfiguration(source, defaultBits: 2);
    using var first = ModelFixture.WithMask(source, firstConfiguration);
    using var second = ModelFixture.WithMask(source, secondConfiguration);

    TestData.SelectMaskRegions(first.Model, first.MaskPath, "Mask0");
    TestData.SelectMaskRegions(second.Model, second.MaskPath, "Mask0");
    Assert.True(ModelFixture.SameNative(
      first.Model.MaskRenderMesh(first.MaskPath), firstConfiguration.DefaultMesh));
    Assert.False(ReferenceEquals(
      first.Model.MaskRenderMesh(first.MaskPath), second.Model.MaskRenderMesh(second.MaskPath)));

    // The same region selection builds independent variant meshes per
    // configuration identity.
    TestData.SelectMaskRegions(first.Model, first.MaskPath, "Mask1");
    TestData.SelectMaskRegions(second.Model, second.MaskPath, "Mask1");
    Assert.False(ReferenceEquals(
      first.Model.MaskRenderMesh(first.MaskPath), second.Model.MaskRenderMesh(second.MaskPath)));
  }

  [TestCase]
  public void GeneratedVariantCacheIsBoundedAndReused()
  {
    ArrayMesh source = TestData.MakeMaskedSourceMesh(3);
    using var fixture = ModelFixture.WithMask(source, TestData.MakeModelMaskConfiguration(source, 3));

    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    ArrayMesh firstVariant = fixture.Model.MaskRenderMesh(fixture.MaskPath)!;
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask2");
    ArrayMesh secondVariant = fixture.Model.MaskRenderMesh(fixture.MaskPath)!;
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    Assert.True(ReferenceEquals(firstVariant, fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    // A third generated variant evicts the oldest entry (original bound: two).
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1", "Mask2");
    ArrayMesh thirdVariant = fixture.Model.MaskRenderMesh(fixture.MaskPath)!;
    Assert.False(ReferenceEquals(thirdVariant, firstVariant));
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask1");
    Assert.False(ReferenceEquals(firstVariant, fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    // Evicted meshes stay valid: each entry held its active render mesh strongly.
    Assert.Equal(1, firstVariant.GetSurfaceCount());
    Assert.Equal(1, thirdVariant.GetSurfaceCount());

    // The next oldest entry is evicted on the following rebuild.
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask2");
    Assert.False(ReferenceEquals(secondVariant, fixture.Model.MaskRenderMesh(fixture.MaskPath)));
  }

  [TestCase]
  public void PieceWriteLeavesAnUnaffectedMaskOwnerUntouched()
  {
    using var fixture = ModelFixture.WithWardrobe(
      configuration: TestData.MakeWardrobeConfigurationWithComponentlessVariantRule(variant: 1),
      accessoryMaskConfiguration: TestData.MakeModelMaskConfiguration(TestData.MakeMaskedSourceMesh(2)));
    var accessoryPath = fixture.AccessoryMaskPath!;
    TestData.SelectMaskRegions(fixture.Model, accessoryPath, "Mask1");
    var render = fixture.Model.MaskRenderMesh(accessoryPath);
    fixture.Model.SetPiece("Garment", false);
    Assert.MaskRegions(fixture.Model, accessoryPath, "Mask1");
    Assert.True(ModelFixture.SameNative(render, fixture.Model.MaskRenderMesh(accessoryPath)));
  }

  [TestCase]
  public void MaskStateIsIndependentAcrossInstances()
  {
    using var configuration = TestData.MakeWardrobeConfiguration();
    using var first = ModelFixture.WithWardrobe(configuration: configuration);
    using var second = ModelFixture.WithWardrobe(configuration: configuration);
    first.Model.OutfitIndex = 1;
    Assert.MaskRegions(first.Model, first.MaskPath, "Mask1");
    Assert.MaskRegions(second.Model, second.MaskPath, "Mask0");
    Assert.True(second.GarmentA!.Visible);
    Assert.False(second.GarmentB!.Visible);
    Assert.Equal(0, second.Model.OutfitIndex);
    Assert.Equal(1, TestData.VisibleTriangles(first.Model.MaskRenderMesh(first.MaskPath)));
  }
}
