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

  // Each inventory getter is exercised as the FIRST operation on its own
  // unstarted fixture: any earlier wardrobe call would parse the configuration
  // and hide a getter that skips EnsureParsed. A malformed configuration must
  // reject repeatedly without publishing a partial inventory, and work after a
  // correction.
  [TestCase]
  public void InventoryGettersParseLazilyAndRejectMalformedConfigurations()
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

    // A mask rule genuinely missing a required field (its 'path'), so the
    // retained validation — not the removed legacy 'body' check — rejects it.
    using var malformed = ModelFixture.WithWardrobe(
      start: false, configuration: ConfigurationWithIncompleteMaskRule());
    Assert.Throws<InvalidOperationException>(() => _ = malformed.Model.Pieces);
    Assert.Throws<InvalidOperationException>(() => _ = malformed.Model.Pieces);
    malformed.Model.WardrobeConfiguration["masks"] = TestData.MakeWardrobeConfiguration()["masks"];
    malformed.Model.Initialize();
    Assert.Equal(1, malformed.Model.Pieces.Count);
  }

  // A mask rule genuinely missing a required field (its 'path'), so the retained
  // validation — not the removed legacy 'body' check — is what rejects it.
  private static Godot.Collections.Dictionary ConfigurationWithIncompleteMaskRule()
  {
    var configuration = TestData.MakeWardrobeConfiguration();
    configuration["masks"] = new Godot.Collections.Array
    {
      new Godot.Collections.Dictionary
      {
        ["name"] = "Mask0", ["index"] = 0,
        ["component"] = "Garment", ["enabled"] = true, ["variant"] = 0,
      },
    };
    return configuration;
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
  public void InvalidWardrobeAuthoringRejectsSetupBeforeAnyWrite()
  {
    // Every invalid authored wardrobe dictionary rejects initialization before
    // any write: garments keep their authored visibility and nothing is stored.
    // (The mask walk renders each mask's own default before the wardrobe
    // derives, so the rejection must leave the defaults in place.)
    void AssertRejectedSetup(Godot.Collections.Dictionary configuration)
    {
      using var fixture = ModelFixture.WithWardrobe(start: false, configuration: configuration);
      Assert.Throws<InvalidOperationException>(() => fixture.Model.Initialize());
      Assert.True(fixture.GarmentA!.Visible);
      Assert.True(fixture.GarmentB!.Visible);
      Assert.Equal(0, fixture.Model.Selections.Count);
    }

    static Godot.Collections.Array MaskRules(Godot.Collections.Dictionary configuration)
      => configuration["masks"].AsGodotArray();
    static Godot.Collections.Dictionary ShirtPiece(Godot.Collections.Dictionary configuration)
      => configuration["components"].AsGodotDictionary()["shirt"].AsGodotDictionary()["pieces"]
        .AsGodotArray()[0].AsGodotDictionary();

    var missingGarment = TestData.MakeOverlappingWardrobeConfiguration();
    ShirtPiece(missingGarment)["path"] = "MissingGarment";
    AssertRejectedSetup(missingGarment);

    var missingMask = TestData.MakeOverlappingWardrobeConfiguration();
    MaskRules(missingMask)[0].AsGodotDictionary()["path"] = "MissingMask";
    AssertRejectedSetup(missingMask);

    var outOfRangeIndex = TestData.MakeOverlappingWardrobeConfiguration();
    MaskRules(outOfRangeIndex)[0].AsGodotDictionary()["index"] = 5;
    AssertRejectedSetup(outOfRangeIndex);

    var mismatchedName = TestData.MakeOverlappingWardrobeConfiguration();
    MaskRules(mismatchedName)[0].AsGodotDictionary()["name"] = "Mask1";
    AssertRejectedSetup(mismatchedName);

    var duplicateOutfits = TestData.MakeOverlappingWardrobeConfiguration();
    duplicateOutfits["variants"] = new Godot.Collections.Array { "outfit0", "outfit0" };
    AssertRejectedSetup(duplicateOutfits);

    var invalidPieceVariant = TestData.MakeOverlappingWardrobeConfiguration();
    ShirtPiece(invalidPieceVariant)["variant"] = 5;
    AssertRejectedSetup(invalidPieceVariant);

    var invalidRuleVariant = TestData.MakeOverlappingWardrobeConfiguration();
    MaskRules(invalidRuleVariant)[0].AsGodotDictionary()["variant"] = 5;
    AssertRejectedSetup(invalidRuleVariant);

    // A configuration without its 'masks' array is incomplete even on a bare root:
    // with the mesh root unconfigured, the failure must come from the wardrobe
    // parse (which runs after the appearance stage), not the mesh-root lookup.
    var incomplete = new CharacterModel
    {
      MeshRoot = new(),
      WardrobeConfiguration = new Godot.Collections.Dictionary
      {
        ["variants"] = new Godot.Collections.Array { "only" },
        ["components"] = new Godot.Collections.Dictionary(),
      },
    };
    bool rejected = false;
    try
    {
      incomplete.Initialize();
    }
    catch (InvalidOperationException failure)
    {
      rejected = true;
      Assert.True(failure.Message.Contains("no 'masks'"),
        $"The bare root rejected for the wrong reason: '{failure.Message}'.");
    }
    Assert.True(rejected, "The incomplete wardrobe configuration did not reject initialization.");
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
