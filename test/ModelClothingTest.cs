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
  public void InvalidWardrobeAuthoringIsReportedBeforeInitialization()
  {
    using var fixture = ModelFixture.WithWardrobe(start: false);
    fixture.Model.WardrobeConfiguration.Variants = ["outfit0", "outfit0"];

    string[] warnings = fixture.Model._GetConfigurationWarnings();
    Assert.True(warnings is not null && warnings.Length > 0,
      "The editor must report invalid wardrobe authoring before an instance initializes.");
    Assert.True(warnings![0].Contains("duplicate outfit"));
    fixture.Model.WardrobeConfiguration.Variants = ["outfit0", "outfit1"];
    Assert.Equal(0, fixture.Model._GetConfigurationWarnings().Length);
    Assert.Equal(0, fixture.Model.Selections.Count);
    Assert.True(fixture.GarmentA!.Visible);
    Assert.True(fixture.GarmentB!.Visible);
  }

  // ------------------------------------------------------------------
  // Uniform selection API
  // ------------------------------------------------------------------

  [TestCase]
  public void SelectOutfitSwapsGarmentsVariantMasksAndRenderCounts()
  {
    using var fixture = ModelFixture.WithWardrobe();
    CharacterModel model = fixture.Model;
    model.SelectOutfit(model.Outfits[1]);
    Assert.Equal(model.Outfits[1], model.CurrentOutfit);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.True(fixture.GarmentB!.Visible);
    Assert.MaskRegions(model, fixture.MaskPath, "Mask1");
    Assert.Equal(1, TestData.VisibleTriangles(model.MaskRenderMesh(fixture.MaskPath)));

    model.SelectOutfit(model.Outfits[0]);
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

    fixture.Model.SetPieceEnabled(piece, false);
    Assert.False(piece.Enabled);
    Assert.False(piece.IsVisible);
    Assert.False(fixture.GarmentA!.Visible);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath);

    fixture.Model.SelectOutfit(fixture.Outfit("outfit1"));
    fixture.Model.ClothingEnabled = false;
    fixture.Model.ClothingEnabled = true;
    Assert.False(piece.Enabled);
    Assert.False(piece.IsVisible);
    Assert.False(fixture.GarmentB!.Visible);
  }

  [TestCase]
  public void ForeignPieceAndOutfitAreRejectedWithoutEffect()
  {
    using var fixture = ModelFixture.WithWardrobe();
    using var other = ModelFixture.WithWardrobe();
    CharacterModel model = fixture.Model;
    ModelClothingPiece foreignPiece = other.Piece("Garment");
    ModelOutfit foreignOutfit = other.Outfit("outfit1");

    Assert.Throws<InvalidOperationException>(() => model.SetPieceEnabled(foreignPiece, false));
    Assert.Throws<InvalidOperationException>(() => model.SelectOutfit(foreignOutfit));
    Assert.True(fixture.GarmentA!.Visible);
    Assert.False(fixture.GarmentB!.Visible);
    Assert.MaskRegions(model, fixture.MaskPath, "Mask0");
    Assert.Equal(0, model.OutfitIndex);
    // The only stored entry is the materialized default; the foreign piece's id
    // is also "Garment", so its rejection leaving it at true proves nothing
    // was stored.
    Assert.Equal(1, model.Selections.Count);
    Assert.True(model.Selections[CharacterModel.PiecesPrefix + "Garment"].AsBool());
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

    fixture.Model.SetPieceEnabled(fixture.Piece("jacket"), false);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    fixture.Model.SetPieceEnabled(fixture.Piece("jacket"), true);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");

    fixture.Model.SetPieceEnabled(fixture.Piece("shirt"), false);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    fixture.Model.SetPieceEnabled(fixture.Piece("jacket"), false);
    Assert.MaskRegions(fixture.Model, fixture.MaskPath);
    fixture.Model.SetPieceEnabled(fixture.Piece("shirt"), true);
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

    model.SelectOutfit(model.Outfits[1]);
    Assert.MaskRegions(model, fixture.MaskPath, "Mask1");
    Assert.MaskRegions(model, fixture.AccessoryMaskPath!, "Mask1");

    model.SelectOutfit(model.Outfits[0]);
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
    missingGarment.Components["shirt"].Pieces[0].Path = "MissingGarment";
    AssertRejectedSetup(missingGarment);

    var missingMask = TestData.MakeOverlappingWardrobeConfiguration();
    missingMask.Masks[0].Path = "MissingMask";
    AssertRejectedSetup(missingMask);

    var outOfRangeIndex = TestData.MakeOverlappingWardrobeConfiguration();
    outOfRangeIndex.Masks[0].Index = 5;
    AssertRejectedSetup(outOfRangeIndex);

    var mismatchedName = TestData.MakeOverlappingWardrobeConfiguration();
    mismatchedName.Masks[0].Name = "Mask1";
    AssertRejectedSetup(mismatchedName);
  }

  [TestCase]
  public void InvalidWardrobeDefinitionsRejectAuthoring()
  {
    // Fixed schema errors belong to the authoring boundary, before a resource
    // is saved. Runtime tests above own actual scene and mask-region bindings.
    Action<ModelWardrobeConfiguration>[] corruptions =
    [
      configuration => configuration.Variants = ["outfit0", "outfit0"],
      configuration => configuration.DefaultVariant = 5,
      configuration => configuration.Components["shirt"].Pieces[0].Variant = 5,
      configuration => configuration.Components["shirt"].Pieces[0].Path = new(),
      configuration => configuration.Masks[0].Variant = 5,
      configuration => configuration.Masks[0].Component = "missing",
      configuration => configuration.Masks[0].Path = new(),
    ];
    foreach (var corrupt in corruptions)
    {
      using var configuration = TestData.MakeOverlappingWardrobeConfiguration();
      configuration.ValidateAuthoring();
      corrupt(configuration);
      Assert.Throws<InvalidOperationException>(configuration.ValidateAuthoring);
    }
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

    Assert.Throws<InvalidOperationException>(() => model.SelectOutfit(model.Outfits[1]));
    Assert.Equal(0, model.OutfitIndex);
    Assert.False(model.Selections.ContainsKey(CharacterModel.OutfitKey));
    Assert.MaskRegions(model, fixture.MaskPath, "Mask0");
    Assert.True(ModelFixture.SameNative(model.MaskRenderMesh(fixture.MaskPath), authoredRender));
  }
}
