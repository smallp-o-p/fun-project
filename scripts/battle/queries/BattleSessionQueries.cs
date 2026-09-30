using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

/// <summary>Per-combatant mission health metrics: the combatant's effective max health at
/// summary creation (including active condition/equipment/buff effects) and the actual
/// health damage taken during the battle. Numeric values are snapshots.</summary>
public sealed record BattleHealthSummary(int MaxHealth, long HealthDamageTaken);

// End-of-battle summary for one faction: outcome, per-combatant kill attribution, and the
// faction's present/dead/wounded roster. Served from the session's stored completion;
// outer dictionaries, roster sets, and nested defeated lists are all frozen.
public sealed record FactionBattleSummary
{
  public required Faction Faction { get; init; }
  public required IReadOnlySet<Combatant> CombatantsPresent { get; init; }
  public required IReadOnlyDictionary<Combatant, IReadOnlyList<Combatant>> DefeatedPerCombatant { get; init; }
  public required BattleOutcome Outcome { get; init; }
  public required IReadOnlySet<Combatant> CombatantsDead { get; init; }
  public required IReadOnlySet<Combatant> CombatantsWounded { get; init; }
  public required int TurnCount { get; init; }
  public IReadOnlyList<Combatant> CapturedEnemies { get; init; } = [];

  /// <summary>
  /// Health report to calculate Combatants' injuries
  /// </summary>
  public IReadOnlyDictionary<Combatant, BattleHealthSummary> HealthByCombatant { get; init; } = new Dictionary<Combatant, BattleHealthSummary>();
}

public sealed class GetFactionEndOfBattleSummary(Faction faction)
  : IBattleSessionQuery<Either<BattleQueryFailure, FactionBattleSummary>>
{
  public Either<BattleQueryFailure, FactionBattleSummary> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Ended)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over."));

    return session.Completed.Match(
      completed => completed.FactionSummaries.TryGetValue(faction, out FactionBattleSummary? summary)
        ? Right<BattleQueryFailure, FactionBattleSummary>(summary)
        : Left<BattleQueryFailure, FactionBattleSummary>(new BattleQueryFailure(
          BattleQueryFailureReason.InvalidBattleState,
          $"Faction {faction.Name} did not participate in this battle.")),
      () => Left<BattleQueryFailure, FactionBattleSummary>(new BattleQueryFailure(
        BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over.")));
  }
}

// The side whose turn it currently is. Total: any session state has an active side
// (during Setup it is the first faction of the order).
public sealed class GetActiveSideQuery : IBattleSessionQuery<Faction>
{
  public Faction Execute(BattleSession session) => session.ActiveSide;
}

// The battle's lifecycle phase. Total by construction.
public sealed class GetBattlePhaseQuery : IBattleSessionQuery<BattlePhase>
{
  public BattlePhase Execute(BattleSession session) => session.Phase;
}
