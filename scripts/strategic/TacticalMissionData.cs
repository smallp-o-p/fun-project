using FunProject.Battle;
using Godot;

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

}
