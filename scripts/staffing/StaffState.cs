using System;
using System.Collections.Generic;
using FunProject.Currencies;

namespace FunProject.Staffing;

/// <summary>Campaign-owned counts and captured role prices/bonuses.</summary>
public sealed class StaffState
{
  private readonly Dictionary<Currency, ulong> _researcherCosts = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<Currency, ulong> _engineerCosts = new(ReferenceEqualityComparer.Instance);

  internal StaffState(StaffingRulesData rules)
  {
    ResearcherSpeedBonus = rules.ResearcherSpeedBonus;
    EngineerSpeedBonus = rules.EngineerSpeedBonus;
    foreach (var (currency, cost) in rules.ResearcherCosts) _researcherCosts.Add(currency, cost);
    foreach (var (currency, cost) in rules.EngineerCosts) _engineerCosts.Add(currency, cost);
  }

  public ulong Researchers { get; private set; }
  public ulong Engineers { get; private set; }
  public float ResearcherSpeedBonus { get; }
  public float EngineerSpeedBonus { get; }
  public float ResearchSpeed => 1 + Researchers * ResearcherSpeedBonus;
  public float ManufacturingSpeed => 1 + Engineers * EngineerSpeedBonus;

  internal sealed record PreparedHire(StaffRole Role, ulong Count, float Speed,
    Godot.Collections.Dictionary<Currency, ulong> Costs);

  internal PreparedHire PrepareHire(StaffRole role, uint quantity, double costModifier = 0)
  {
    var (count, bonus, costs) = role switch
    {
      StaffRole.Researcher => (Researchers, ResearcherSpeedBonus, _researcherCosts),
      StaffRole.Engineer => (Engineers, EngineerSpeedBonus, _engineerCosts),
      _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
    var total = new Godot.Collections.Dictionary<Currency, ulong>();
    decimal factor = (decimal)Math.Clamp(1 + costModifier, 0, (double)ulong.MaxValue);
    foreach (var (currency, cost) in costs)
    {
      decimal raw = (decimal)cost * quantity;
      total.Add(currency, factor >= 1 && raw >= ulong.MaxValue / factor
        ? ulong.MaxValue : (ulong)Math.Min(ulong.MaxValue, decimal.Ceiling(raw * factor)));
    }
    ulong nextCount = count > ulong.MaxValue - quantity ? ulong.MaxValue : count + quantity;
    return new PreparedHire(role, nextCount, 1 + nextCount * bonus, total);
  }

  internal void Commit(PreparedHire prepared)
  {
    if (prepared.Role == StaffRole.Researcher) Researchers = prepared.Count;
    else Engineers = prepared.Count;
  }
}
