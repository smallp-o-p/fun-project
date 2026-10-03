using Godot;

namespace FunProject.Models;

/// <summary>
/// One clothing piece of a <see cref="CharacterModel"/>: an authored component
/// whose garments the model hides and restores together.
/// </summary>
public sealed class ModelClothingPiece
{
  internal ModelClothingPiece(CharacterModel owner, StringName id)
  {
    _model = owner;
    Id = id;
  }

  private readonly CharacterModel _model;

  /// <summary>The authored component name; the stable lookup key of this piece.</summary>
  public StringName Id { get; }

  /// <summary>Human-facing piece name; the authored component name.</summary>
  public string DisplayName => Id.ToString();

  /// <summary>
  /// The saved per-piece selection. The wardrobe's master clothing switch only
  /// suppresses its effect, so hiding and restoring the outfit preserves it.
  /// </summary>
  public bool Enabled
  {
    get => _model.GetPiece(Id);
    set => _model.SetPiece(Id, value);
  }

  /// <summary>
  /// Effective visibility: the piece is selected, clothing is enabled, and the
  /// current outfit owns at least one garment of this piece.
  /// </summary>
  public bool IsVisible => _model.ComponentActive(Id);
}
