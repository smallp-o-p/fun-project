using Godot;

namespace FunProject.Progression;

/// <summary>Grants a battle ability (inert until the ability system consumes it).</summary>
[GlobalClass]
public partial class AbilityGrantUpgradeEffectData : UpgradeEffectData
{
  [Export] required public AbilityData Ability { get; set; }
}
