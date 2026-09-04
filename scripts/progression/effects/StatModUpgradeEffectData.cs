using FunProject.Stats;
using Godot;

namespace FunProject.Progression;

/// <summary>Grants permanent stat mods that fold into the unit's stat contributions.</summary>
[GlobalClass]
public partial class StatModUpgradeEffectData : UpgradeEffectData
{
  [Export] public Godot.Collections.Array<StatMod> StatMods { get; set; } = [];
}
