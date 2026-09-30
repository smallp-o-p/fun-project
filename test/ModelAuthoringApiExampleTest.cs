using System;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class ModelAuthoringApiExampleTest
{
  // The documentation example from docs/model-controls.md, verbatim: a required
  // application that resolves the clothing capability through Option.Match and
  // throws when it is absent. Changing the documented API means changing this
  // method and the docs together.
  private static void Preview(CharacterModel model, StringName pieceId)
  {
    ModelClothingPiece piece = model.FindPiece(pieceId).Match(
      found => found,
      () => throw new InvalidOperationException($"Missing clothing piece '{pieceId}'."));
    model.SetPieceEnabled(piece, false);
    model.ClothingEnabled = false;
  }

  [TestCase]
  public void DocumentedExampleDrivesDiscoveredSyntheticCapabilities()
  {
    using var fixture = ModelFixture.WithWardrobe();
    CharacterModel model = fixture.Model;
    StringName pieceId = model.Pieces[0].Id;

    Preview(model, pieceId);

    Assert.False(fixture.Piece(pieceId).Enabled);
    Assert.False(fixture.Piece(pieceId).IsVisible);
    Assert.False(model.ClothingEnabled);

    // Optional applications inspect the Option instead of throwing; the None
    // cases are ordinary values, while the required example throws.
    Assert.True(model.FindPiece("missing").IsNone);
    Assert.Throws<InvalidOperationException>(() => Preview(model, "missing"));
  }

  [TestCase("res://scenes/models/ZhuYuan/ZhuYuan.scn")]
  [TestCase("res://scenes/models/Trigger/Trigger.scn")]
  public void DocumentedExampleDrivesDiscoveredSceneCapabilities(string scenePath)
  {
    using var fixture = ModelFixture.FromScene(scenePath);
    CharacterModel model = fixture.Model;
    // The caller discovers the piece ID from the model's own inventory, exactly
    // as the guide prescribes; the guide never hard-codes it.
    StringName pieceId = model.Pieces[0].Id;

    Preview(model, pieceId);

    Assert.False(model.FindPiece(pieceId).Match(
      piece => piece.Enabled,
      () => throw new InvalidOperationException("The documented example's piece vanished.")));
    Assert.False(model.ClothingEnabled);
  }
}
