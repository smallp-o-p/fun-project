using FunProject.Battle;
using Godot;
using System;

namespace FunProject.Strategic;

/// <summary>Authored tactical mission: which battle type hosts it, its capacity envelope,
/// which non-player slot the generated enemy force replaces, and the enemy pools.</summary>
[GlobalClass]
public partial class TacticalMissionData : Resource
{
  [Export] public required BattleTypeData BattleType { get; set; }

  [Export] public required BattleSizeData Size { get; set; }

  /// <summary>Index of the battle type's faction slot whose authored roster the generated
  /// force replaces; must be a non-player slot.</summary>
  [Export] public int EnemyFactionIndex { get; set; } = 1;

  /// <summary>Pool ordinary enemies are drawn from, uniformly with replacement.</summary>
  [Export] public Godot.Collections.Array<UnitLoadoutData> OrdinaryEnemies { get; set; } = [];

  /// <summary>Mandatory entries appended after the ordinary draw; each contributes exactly
  /// one unit, additively to the drawn ordinary count.</summary>
  [Export] public Godot.Collections.Array<UnitLoadoutData> SpecialEnemies { get; set; } = [];

  /// <summary>Authoring guard: required resources, valid distinct enemy slot, ordinary pool
  /// present whenever the draw can be positive, and valid loadout definitions.</summary>
  public void Validate()
  {
    if (BattleType is null)
      throw new InvalidOperationException($"{nameof(TacticalMissionData)} requires a {nameof(BattleType)}.");
    if (Size is null)
      throw new InvalidOperationException($"{nameof(TacticalMissionData)} requires a {nameof(Size)}.");
    Size.Validate();
    if (EnemyFactionIndex < 0 || EnemyFactionIndex >= BattleType.Factions.Count)
      throw new InvalidOperationException(
        $"{nameof(TacticalMissionData)} {nameof(EnemyFactionIndex)} {EnemyFactionIndex} is out of range for {BattleType.Factions.Count} factions.");
    if (EnemyFactionIndex == BattleType.PlayerFactionIndex)
      throw new InvalidOperationException(
        $"{nameof(TacticalMissionData)} {nameof(EnemyFactionIndex)} must not target the battle type's player faction slot.");
    if (Size.MaxEnemyUnits > 0 && OrdinaryEnemies.Count == 0)
      throw new InvalidOperationException(
        $"{nameof(TacticalMissionData)} can draw up to {Size.MaxEnemyUnits} ordinary enemies but has an empty {nameof(OrdinaryEnemies)} pool.");
    ValidateEntries(OrdinaryEnemies, nameof(OrdinaryEnemies));
    ValidateEntries(SpecialEnemies, nameof(SpecialEnemies));
  }

  private static void ValidateEntries(Godot.Collections.Array<UnitLoadoutData> entries, string poolName)
  {
    for (int index = 0; index < entries.Count; index++)
    {
      if (entries[index] is null)
        throw new InvalidOperationException(
          $"{nameof(TacticalMissionData)} {poolName}[{index}] is null.");
      entries[index].Validate();
    }
  }
}
