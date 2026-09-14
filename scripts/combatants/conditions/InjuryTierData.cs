using Godot;

namespace FunProject.Combatants.Conditions;

[GlobalClass]
public partial class InjuryTierData : ConditionTierData
{
  [Export(PropertyHint.Range, "1.0,100.0,1.0")] public float MinimumDamagePercent { get; set; }
}
