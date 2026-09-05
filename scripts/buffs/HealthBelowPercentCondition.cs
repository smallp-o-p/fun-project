using FunProject.Battle;
using Godot;

namespace FunProject.Buffs;

/// <summary>True while current health is strictly below Percent% of effective max health.</summary>
[GlobalClass]
public partial class HealthBelowPercentCondition : BuffCondition
{
  [Export(PropertyHint.Range, "0,100")] public float Percent { get; set; } = 50f;

  internal override bool IsMet(BattleSession session, BattleUnitState unit)
    => unit.CurrentHealth < unit.MaxHealth * (Percent / 100f);
}
