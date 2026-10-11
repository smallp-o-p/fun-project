using System;
using System.Collections.Generic;
using FunProject.Currencies;
using FunProject.Strategic;

namespace FunProject.Staffing;

/// <summary>Campaign-owned counts and captured role prices/bonuses.</summary>
public sealed class StaffState
{
  private readonly Dictionary<Currency, ulong> _researcherCosts = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<Currency, ulong> _engineerCosts = new(ReferenceEqualityComparer.Instance);

  internal StaffState(StaffingRulesData? rules)
  {
    IsConfigured = rules is not null;
    ResearcherBonusUnits = rules?.ResearcherBonusUnits ?? 0;
    EngineerBonusUnits = rules?.EngineerBonusUnits ?? 0;
    if (rules is null) return;
    foreach (var (currency, cost) in rules.ResearcherCosts) _researcherCosts.Add(currency, cost);
    foreach (var (currency, cost) in rules.EngineerCosts) _engineerCosts.Add(currency, cost);
  }

  public bool IsConfigured { get; }
  public int Researchers { get; private set; }
  public int Engineers { get; private set; }
  public uint ResearcherBonusUnits { get; }
  public uint EngineerBonusUnits { get; }
  public long ResearchRateUnits => Rate(Researchers, ResearcherBonusUnits);
  public long ManufacturingRateUnits => Rate(Engineers, EngineerBonusUnits);

  internal readonly record struct PreparedHire(StaffRole Role, int Count,
    long OldRateUnits, long NewRateUnits, Godot.Collections.Dictionary<Currency, ulong> Costs);

  internal Either<StaffHireFailure, PreparedHire> PrepareHire(StaffRole role, int quantity)
  {
    if (quantity <= 0) return StaffHireFailure.InvalidQuantity;
    if (!IsConfigured) return StaffHireFailure.NotConfigured;
    var (count, bonus, costs) = role switch
    {
      StaffRole.Researcher => (Researchers, ResearcherBonusUnits, _researcherCosts),
      StaffRole.Engineer => (Engineers, EngineerBonusUnits, _engineerCosts),
      _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
    if (quantity > int.MaxValue - count) return StaffHireFailure.Overflow;
    var total = new Godot.Collections.Dictionary<Currency, ulong>();
    foreach (var (currency, cost) in costs)
    {
      if (cost > ulong.MaxValue / (ulong)quantity) return StaffHireFailure.Overflow;
      total.Add(currency, checked(cost * (ulong)quantity));
    }
    int nextCount = checked(count + quantity);
    return new PreparedHire(role, nextCount, Rate(count, bonus), Rate(nextCount, bonus), total);
  }

  // Payment and matching job settlement are already prepared; no callbacks here.
  internal void Commit(PreparedHire prepared)
  {
    if (prepared.Role == StaffRole.Researcher) Researchers = prepared.Count;
    else Engineers = prepared.Count;
  }

  private static long Rate(int count, uint bonus)
    => checked(WorkProgress.RateScale + (long)count * bonus);
}
