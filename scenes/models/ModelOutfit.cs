using Godot;

namespace FunProject.Models;

/// <summary>
/// One outfit of a <see cref="CharacterModel"/>, identified by the authored
/// variant label.
/// </summary>
public sealed class ModelOutfit
{
  internal ModelOutfit(CharacterModel owner, StringName id, int variantIndex)
  {
    _model = owner;
    Id = id;
    _variantIndex = variantIndex;
  }

  private readonly CharacterModel _model;
  private readonly int _variantIndex;

  /// <summary>The authored variant label; the stable lookup key of this outfit.</summary>
  public StringName Id { get; }

  /// <summary>Human-facing outfit name; the authored variant label.</summary>
  public string DisplayName => Id.ToString();

  /// <summary>Selects this imported outfit on the instance that exposes it.</summary>
  public void Select() => _model.OutfitIndex = _variantIndex;
}
