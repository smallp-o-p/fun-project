using FunProject.Core;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;

namespace FunProject.Buffs;

/// <summary>
/// A shared authored condition and its stat/damage contributions.
/// BattleUnitState tracks activation separately for each grant.
/// </summary>
[GlobalClass]
public partial class Buff : NamedEntityData
{
  // Godot deserialization bypasses required; unit construction checks this reference.
  [Export] required public BuffCondition Condition { get; set; }
  [Export] public Godot.Collections.Array<StatMod> StatMods { get; set; } = [];
  [Export] public Godot.Collections.Array<DamageBundleMod> DamageMods { get; set; } = [];
}
