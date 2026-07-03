using FunProject.Core;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;

namespace FunProject.Buffs;

/// <summary>
/// An authored buff: an activation condition plus the contributions granted while active.
/// StatMods feed unit-level stat resolution; DamageMods fold into weapon damage emission.
/// Identity is this resource — the runtime dedupes a buff granted by several sources.
/// </summary>
[GlobalClass]
public partial class BuffData : NamedEntityData
{
  // required matches the codebase convention for mandatory authored references
  // (WeaponData.Frame, CombatantData stats); the runtime Buff ctor still guards null
  // because Godot editor deserialization bypasses C# required enforcement.
  [Export] required public BuffConditionData Condition { get; set; }
  [Export] public Godot.Collections.Array<StatMod> StatMods { get; set; } = [];
  [Export] public Godot.Collections.Array<DamageBundleMod> DamageMods { get; set; } = [];
}
