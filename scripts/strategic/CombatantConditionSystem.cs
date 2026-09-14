using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Combatants.Conditions;
using FunProject.Stats;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Strategic;

public sealed record FatigueState(int Tier, long RecoveryTick);

public sealed record InjuryState(int Tier, long RecoveryTick);

/// <summary>
/// System to track injury and fatigue state for Combatants.
/// Fatigue and Injury are two separate systems.
/// A fatigued unit that returns injured accumulates more fatigue, on top of the injury recovery time.
/// 
/// </summary>
public sealed class CombatantConditionSystem
{
  private readonly Godot.Collections.Array<InjuryTierData> _injuryTiers;
  private readonly Godot.Collections.Array<ConditionTierData> _fatigueTiers;
  private readonly Dictionary<Combatant, InjuryState> _injuries = [];
  private readonly Dictionary<Combatant, FatigueState> _fatigues = [];

  public CombatantConditionSystem(CombatantConditionRulesData? rules = null)
  {
    rules ??= CombatantConditionRulesData.Shared;
    rules.ValidateAndSort();
    _injuryTiers = rules.InjuryTiers;
    _fatigueTiers = rules.FatigueTiers;
  }

  /// <summary>Active injury record for a combatant, or <c>None</c> when healthy; a missing
  /// entry stays missing and the read never inserts one.</summary>
  public Option<InjuryState> GetInjury(Combatant combatant)
    => _injuries.TryGetValue(combatant, out var injury) ? Some(injury) : None;

  /// <summary>Active fatigue record for a combatant, or <c>None</c> when fresh; a missing
  /// entry stays missing and the read never inserts one.</summary>
  public Option<FatigueState> GetFatigue(Combatant combatant)
    => _fatigues.TryGetValue(combatant, out var fatigue) ? Some(fatigue) : None;

  public string InjuryName(int position)
    => position == 0 ? "Healthy" : InjuryTierAt(position).Name;

  public string FatigueName(int position)
    => position == 0 ? "Fresh" : FatigueTierAt(position).Name;

  /// <summary>Uninjured and below the ladder's top fatigue tier; the flag makes every
  /// position eligible.</summary>
  public bool CanDeploy(Combatant combatant, bool allowUnfitDeployment)
    => allowUnfitDeployment
      || (GetInjury(combatant).IsNone
        && GetFatigue(combatant).Match(
          fatigue => fatigue.Tier < _fatigueTiers.Count, () => true));

  /// <summary>Stat mods of the current injury tier and current fatigue tier only, plus the
  /// unconditional one-hit-point health floor while any injury is active — authored tiers
  /// cannot leave a wounded combatant with zero max health.</summary>
  public IReadOnlyList<StatMod> StatContributions(Combatant combatant)
  {
    List<StatMod> contributions = [];
    if (GetInjury(combatant).Case is InjuryState injury)
    {
      contributions.AddRange(InjuryTierAt(injury.Tier).StatMods);
      contributions.Add(MinimumHealthFloor);
    }
    if (GetFatigue(combatant).Case is FatigueState fatigue)
      contributions.AddRange(FatigueTierAt(fatigue.Tier).StatMods);
    return contributions;
  }

  /// <summary>
  /// Calculate a Combatant's injury and fatigue when returning from battle.
  /// </summary>
  internal void ApplyMissionReturn(Combatant combatant, long healthDamageTaken, int maxHealth, long tick)
  {
    Option<InjuryState> currentInjury = GetInjury(combatant);
    Option<FatigueState> currentFatigue = GetFatigue(combatant);

    // Zero only starts the arithmetic for an absent record: positive damage adds earned
    // tiers onto it, so a healthy unit hit below the first authored threshold (or under an
    // empty injury ladder) creates no injury, while an already injured one re-arms its tier.
    Option<InjuryState> nextInjury = currentInjury;
    if (healthDamageTaken != 0)
    {
      int tier = Math.Min(_injuryTiers.Count,
        (currentInjury.Case is InjuryState prior ? prior.Tier : 0)
        + EarnedInjuryTiers(healthDamageTaken, maxHealth));
      nextInjury = tier == 0
        ? None
        : new InjuryState(tier, checked(tick + CampaignGameState.TicksFromDays(InjuryTierAt(tier).RecoveryDays)));
    }

    // The accrual gate is the injury record BEFORE this return, never the newly computed
    // one. An empty fatigue ladder caps the climb at zero tiers, creating no record.
    Option<FatigueState> nextFatigue = currentFatigue;
    if (currentInjury.IsNone)
    {
      int tier = Math.Min(_fatigueTiers.Count,
        (currentFatigue.Case is FatigueState prior ? prior.Tier : 0) + 1);
      nextFatigue = tier == 0
        ? None
        : new FatigueState(tier, checked(tick + CampaignGameState.TicksFromDays(FatigueTierAt(tier).RecoveryDays)));
    }

    if (nextInjury.Case is InjuryState activeInjury)
      _injuries[combatant] = activeInjury;
    else
      _injuries.Remove(combatant);
    if (nextFatigue.Case is FatigueState activeFatigue)
      _fatigues[combatant] = activeFatigue;
    else
      _fatigues.Remove(combatant);
  }

  internal IReadOnlyList<Combatant> Recover(IReadOnlyList<Combatant> roster, long tick)
  {
    List<Combatant> recovered = [];
    foreach (Combatant combatant in roster)
    {
      Option<InjuryState> currentInjury = GetInjury(combatant);
      Option<FatigueState> currentFatigue = GetFatigue(combatant);

      Option<InjuryState> nextInjury = currentInjury;
      while (nextInjury.Case is InjuryState injury && tick >= injury.RecoveryTick)
        nextInjury = injury.Tier == 1
          ? None
          : new InjuryState(injury.Tier - 1,
            checked(injury.RecoveryTick + CampaignGameState.TicksFromDays(InjuryTierAt(injury.Tier - 1).RecoveryDays)));

      Option<FatigueState> nextFatigue = currentFatigue;
      while (nextFatigue.Case is FatigueState fatigue && tick >= fatigue.RecoveryTick)
        nextFatigue = fatigue.Tier == 1
          ? None
          : new FatigueState(fatigue.Tier - 1,
            checked(fatigue.RecoveryTick + CampaignGameState.TicksFromDays(FatigueTierAt(fatigue.Tier - 1).RecoveryDays)));

      if (nextInjury == currentInjury && nextFatigue == currentFatigue)
        continue;
      if (nextInjury.Case is InjuryState activeInjury)
        _injuries[combatant] = activeInjury;
      else
        _injuries.Remove(combatant);
      if (nextFatigue.Case is FatigueState activeFatigue)
        _fatigues[combatant] = activeFatigue;
      else
        _fatigues.Remove(combatant);
      recovered.Add(combatant);
    }
    return recovered;
  }

  // Shared immutable resource by convention, exactly like authored StatMods.
  private static readonly HealthStatMod MinimumHealthFloor = CreateMinimumHealthFloor();

  private static HealthStatMod CreateMinimumHealthFloor()
  {
    var floor = new HealthStatMod();
    floor.AddModifier(StatModifier.CapMin(1));
    return floor;
  }

  private int EarnedInjuryTiers(long healthDamageTaken, int maxHealth)
  {
    // Cross-multiplied doubles with no intermediate ratio: 29/100*100 drifts below an
    // exact 29 threshold, while 29*100 vs 29*100 compares equal.
    double scaledDamage = healthDamageTaken * 100.0;
    int earned = 0;
    for (int i = 0; i < _injuryTiers.Count; i++)
    {
      double threshold = _injuryTiers[i].MinimumDamagePercent;
      if (threshold * maxHealth <= scaledDamage)
        earned++;
    }
    return earned;
  }

  private InjuryTierData InjuryTierAt(int position) => _injuryTiers[position - 1];

  private ConditionTierData FatigueTierAt(int position) => _fatigueTiers[position - 1];
}
