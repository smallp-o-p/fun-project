using System;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class ModelClothingTest
{
  // ------------------------------------------------------------------
  // Inventory and identity
  // ------------------------------------------------------------------

  [TestCase]
  public void WardrobeExposesDiscoveredPiecesAndOutfits()
  {
    using var fixture = ModelFixture.WithWardrobe();
    CharacterModel model = fixture.Model;
    Assert.Equal(1, model.Pieces.Count);
    Assert.Equal("Garment", model.Pieces[0].Id.ToString());
    Assert.Equal("Garment", model.Pieces[0].DisplayName);
    Assert.Equal(2, model.Outfits.Count);
    Assert.Equal("outfit0", model.Outfits[0].Id.ToString());
    Assert.Equal("outfit1", model.Outfits[1].Id.ToString());

    // Lookups return the discovered entries themselves; unknown ids are None.
    Assert.True(model.FindPiece("Garment").Match(
      piece => ReferenceEquals(piece, model.Pieces[0]), () => false));
    Assert.True(model.FindOutfit("outfit1").Match(
      outfit => ReferenceEquals(outfit, model.Outfits[1]), () => false));
    Assert.False(model.FindPiece("Bogus").Match(_ => true, () => false));
    Assert.False(model.FindOutfit("outfit9").Match(_ => true, () => false));
    Assert.Equal(model.Outfits[0], model.CurrentOutfit);
  }

  // Each inventory getter is the first operation on its unstarted fixture:
  // inventories must be available without initializing scene bindings.
  [TestCase]
  public void InventoryGettersExposeAuthoredEntriesBeforeInitialization()
  {
    using var piecesFixture = ModelFixture.WithWardrobe(start: false);
    CharacterModel piecesModel = piecesFixture.Model;
    Assert.Equal(1, piecesModel.Pieces.Count);
    Assert.Equal("Garment", piecesModel.Pieces[0].Id.ToString());
    Assert.True(piecesModel.FindPiece("Garment").Match(
      piece => ReferenceEquals(piece, piecesModel.Pieces[0]), () => false));

    using var outfitsFixture = ModelFixture.WithWardrobe(start: false);
    CharacterModel outfitsModel = outfitsFixture.Model;
    Assert.Equal(2, outfitsModel.Outfits.Count);
    Assert.Equal("outfit0", outfitsModel.Outfits[0].Id.ToString());
    Assert.True(outfitsModel.FindOutfit("outfit1").Match(
      outfit => ReferenceEquals(outfit, outfitsModel.Outfits[1]), () => false));
  }

  [TestCase]
  public void CatalogAndInventoriesCannotBeEditedByConsumers()
  {
    using var fixture = ModelFixture.WithWardrobe();
    var catalog = fixture.Model.WardrobeConfiguration;
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<string>)catalog.Variants)[0] = "changed");
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IDictionary<string, ModelWardrobeComponent>)catalog.Components).Clear());
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ModelWardrobeGarment>)catalog.Components["Garment"].Pieces).Clear());
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ModelWardrobeMaskRule>)catalog.Masks).Clear());
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ModelClothingPiece>)fixture.Model.Pieces).Clear());
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ModelOutfit>)fixture.Model.Outfits).Clear());
    Assert.Equal("outfit0", fixture.Model.Outfits[0].Id.ToString());
    fixture.Model.Pieces[0].Enabled = false;
    Assert.False(fixture.GarmentA!.Visible);
    fixture.Model.Pieces[0].Enabled = true;
    Assert.True(fixture.GarmentA.Visible);
  }

  [TestCase]
  public void DiscoveredControlsSelectTheirOwnInstanceWithoutForeignHandleInputs()
  {
    using var first = ModelFixture.WithWardrobe();
    using var second = ModelFixture.WithWardrobe();
    first.Model.Pieces[0].Enabled = false;
    Assert.False(first.GarmentA!.Visible);
    Assert.True(second.GarmentA!.Visible);
    second.Model.Outfits[1].Select();
    Assert.Equal(0, first.Model.OutfitIndex);
    Assert.Equal(1, second.Model.OutfitIndex);
    Assert.True(typeof(CharacterModel).GetMethod("SetPieceEnabled") is null);
    Assert.True(typeof(CharacterModel).GetMethod("SelectOutfit") is null);
  }

  [TestCase]
  public void SelectionBeforeImportedChildIsAttachedUsesCatalogAtInitialization()
  {
    using var fixture = ModelFixture.WithWardrobe(start: false);
    var imported = fixture.Model.GetNode("Model");
    fixture.Model.RemoveChild(imported);
    fixture.Model.ClothingEnabled = false;
    fixture.Model.AddChild(imported);
    fixture.Model.Initialize();
    Assert.Equal(1, fixture.Model.Pieces.Count);
    Assert.Equal(2, fixture.Model.Outfits.Count);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.False(fixture.GarmentB!.Visible);
    fixture.Model.ClothingEnabled = true;
    Assert.True(fixture.GarmentA.Visible);
    Assert.False(fixture.GarmentB.Visible);
  }

  [TestCase]
  public void ImportedCatalogHasNoInspectorAuthoringSurface()
  {
    using var fixture = ModelFixture.WithWardrobe();
    foreach (var property in fixture.Model.GetPropertyList())
      Assert.False(property["name"].AsString() == "WardrobeConfiguration",
        "The model must obtain its catalog from the import, not an editable resource slot.");
    GodotObject[] definitions = [fixture.Model.WardrobeConfiguration,
      fixture.Model.WardrobeConfiguration.Components["Garment"],
      fixture.Model.WardrobeConfiguration.Components["Garment"].Pieces[0],
      fixture.Model.WardrobeConfiguration.Masks[0]];
    foreach (var definition in definitions)
      foreach (var property in definition.GetPropertyList())
      {
        var usage = (PropertyUsageFlags)property["usage"].AsInt64();
        Assert.False(usage.HasFlag(PropertyUsageFlags.ScriptVariable) && usage.HasFlag(PropertyUsageFlags.Editor),
          "Generated catalog fields must be storage-only.");
      }
  }

  // ------------------------------------------------------------------
  // Uniform selection API
  // ------------------------------------------------------------------

  [TestCase]
  public void SelectOutfitSwapsGarmentsVariantMasksAndRenderCounts()
  {
    using var fixture = ModelFixture.WithWardrobe();
    CharacterModel model = fixture.Model;
    model.Outfits[1].Select();
    Assert.Equal(model.Outfits[1], model.CurrentOutfit);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.True(fixture.GarmentB!.Visible);
    Assert.MaskRegions(model, fixture.MaskPath, "Mask1");
    Assert.Equal(1, TestData.VisibleTriangles(model.MaskRenderMesh(fixture.MaskPath)));

    model.Outfits[0].Select();
    Assert.Equal(model.Outfits[0], model.CurrentOutfit);
    Assert.True(fixture.GarmentA!.Visible);
    Assert.False(fixture.GarmentB!.Visible);
    Assert.MaskRegions(model, fixture.MaskPath, "Mask0");
    Assert.Equal(1, TestData.VisibleTriangles(model.MaskRenderMesh(fixture.MaskPath)));
  }

  [TestCase]
  public void ClothingToggleHidesAndRestoresTheEntireOutfit()
  {
    using var fixture = ModelFixture.WithWardrobe();
    ModelClothingPiece piece = fixture.Piece("Garment");
    fixture.Model.ClothingEnabled = false;
    Assert.True(piece.Enabled);
    Assert.False(piece.IsVisible);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.False(fixture.GarmentB!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath);
    Assert.Equal(2, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));

    fixture.Model.ClothingEnabled = true;
    Assert.True(piece.Enabled);
    Assert.True(piece.IsVisible);
    Assert.True(fixture.GarmentA!.Visible);
    Assert.False(fixture.GarmentB!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
  }

  // A disabled piece stays disabled through outfit switches and master-toggle
  // round trips; playback independence is owned by the real-scene animation suite
  // (ClothingChangeKeepsPresetWeightsAndPlayback).
  [TestCase]
  public void PieceDisableSurvivesOutfitSwitchAndToggle()
  {
    using var fixture = ModelFixture.WithWardrobe();
    ModelClothingPiece piece = fixture.Piece("Garment");
    Assert.True(piece.Enabled);
    Assert.True(piece.IsVisible);

    piece.Enabled = false;
    Assert.False(piece.Enabled);
    Assert.False(piece.IsVisible);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath);

    fixture.Outfit("outfit1").Select();
    fixture.Model.ClothingEnabled = false;
    fixture.Model.ClothingEnabled = true;
    Assert.False(piece.Enabled);
    Assert.False(piece.IsVisible);
    Assert.False(fixture.GarmentB!.Visible);
  }

  // ------------------------------------------------------------------
  // Overlapping mask regions
  // ------------------------------------------------------------------

  [TestCase]
  public void OverlappingPiecesOrTheirSharedBodyRegion()
  {
    // Two variant-independent pieces whose enabled rules both cover bit zero:
    // coordinated derivation ORs both contributions instead of applying
    // last-rule-wins, so the shared region stays masked until both are removed.
    // Disabling the LATER rule's piece first pins the rule order: the derivation
    // must re-derive the whole owner group, so the earlier shirt rule that
    // stays active retains the shared region.
    using var fixture = ModelFixture.WithWardrobe(
      configuration: TestData.MakeOverlappingWardrobeConfiguration());
    Assert.Equal(2, fixture.Model.Pieces.Count);
    Assert.True(fixture.GarmentA!.Visible);
    Assert.True(fixture.GarmentB!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");

    fixture.Piece("jacket").Enabled = false;
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    fixture.Piece("jacket").Enabled = true;
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");

    fixture.Piece("shirt").Enabled = false;
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    fixture.Piece("jacket").Enabled = false;
    Assert.MaskRegions(fixture.Model, fixture.MaskPath);
    fixture.Piece("shirt").Enabled = true;
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
  }

  [TestCase]
  public void ComponentlessVariantRuleFollowsOutfitSelection()
  {
    using var fixture = ModelFixture.WithWardrobe(
      configuration: TestData.MakeWardrobeConfigurationWithComponentlessVariantRule(variant: 1),
      accessoryMaskConfiguration: TestData.MakeModelMaskConfiguration(TestData.MakeMaskedSourceMesh(2)));
    CharacterModel model = fixture.Model;
    // The accessory mask carries only the componentless variant-1 rule, so its
    // region must follow every outfit derivation even though no component rule
    // shares its path.
    Assert.MaskRegions(fixture.Model, fixture.AccessoryMaskPath!);

    model.Outfits[1].Select();
    Assert.MaskRegions(model, fixture.MaskPath, "Mask1");
    Assert.MaskRegions(model, fixture.AccessoryMaskPath!, "Mask1");

    model.Outfits[0].Select();
    Assert.MaskRegions(model, fixture.MaskPath, "Mask0");
    Assert.MaskRegions(model, fixture.AccessoryMaskPath!);
  }

  // ------------------------------------------------------------------
  // Setup-time required-data failures
  // ------------------------------------------------------------------

  [TestCase]
  public void RequiredWardrobeBindingsRejectSetupBeforeAnyWrite()
  {
    void AssertRejectedSetup(ModelWardrobeConfiguration configuration)
    {
      using var fixture = ModelFixture.WithWardrobe(start: false, configuration: configuration);
      Assert.Throws<InvalidOperationException>(() => fixture.Model.Initialize());
      Assert.True(fixture.GarmentA!.Visible);
      Assert.True(fixture.GarmentB!.Visible);
      Assert.Equal(0, fixture.Model.Selections.Count);
    }

    var missingGarment = TestData.MakeOverlappingWardrobeConfiguration();
    missingGarment.Components["shirt"].Pieces[0].Set("_path", new NodePath("MissingGarment"));
    AssertRejectedSetup(missingGarment);

    var missingMask = TestData.MakeOverlappingWardrobeConfiguration();
    missingMask.Masks[0].Set("_path", new NodePath("MissingMask"));
    AssertRejectedSetup(missingMask);

    var outOfRangeIndex = TestData.MakeOverlappingWardrobeConfiguration();
    outOfRangeIndex.Masks[0].Set("_index", 5);
    AssertRejectedSetup(outOfRangeIndex);

    var mismatchedName = TestData.MakeOverlappingWardrobeConfiguration();
    mismatchedName.Masks[0].Set("_name", new StringName("Mask1"));
    AssertRejectedSetup(mismatchedName);
  }

  // ------------------------------------------------------------------
  // Preflight of live writes after initialization
  // ------------------------------------------------------------------

  [TestCase]
  public void MissingGarmentAfterInitializationRejectsWriteWithoutChanges()
  {
    // A garment node disappearing after initialization (a torn-down subtree)
    // is the predictable live failure: the write preflights, stores nothing,
    // and leaves garments, masks, and render output untouched. (The whole-
    // dictionary Selections replacement door additionally reverts its own
    // assignment — ModelMaskWardrobeTest.SelectionsReplacementsApplyByLifecycle.)
    using var fixture = ModelFixture.WithWardrobe();
    CharacterModel model = fixture.Model;
    ArrayMesh authoredRender = model.MaskRenderMesh(fixture.MaskPath)!;
    fixture.GarmentA!.Free();

    Assert.Throws<InvalidOperationException>(() => model.Outfits[1].Select());
    Assert.Equal(0, model.OutfitIndex);
    Assert.False(model.Selections.ContainsKey(CharacterModel.OutfitKey));
    Assert.MaskRegions(model, fixture.MaskPath, "Mask0");
    Assert.True(ModelFixture.SameNative(model.MaskRenderMesh(fixture.MaskPath), authoredRender));
  }
}
