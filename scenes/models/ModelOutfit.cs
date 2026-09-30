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
    Owner = owner;
    Id = id;
    VariantIndex = variantIndex;
  }

  internal CharacterModel Owner { get; }

  /// <summary>The authored variant label; the stable lookup key of this outfit.</summary>
  public StringName Id { get; }

  /// <summary>Human-facing outfit name; the authored variant label.</summary>
  public string DisplayName => Id.ToString();

  /// <summary>The wardrobe variant index this outfit selects.</summary>
  internal int VariantIndex { get; }
}
