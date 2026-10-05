using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

/// <summary>Per-combatant mission health metrics: the combatant's effective max health at
/// summary creation (including active condition/equipment/buff effects) and the actual
/// health damage taken during the battle. Numeric values are snapshots.</summary>
public sealed record BattleHealthSummary(int MaxHealth, long HealthDamageTaken);

// End-of-battle summary for one faction: outcome, per-combatant kill attribution, and the
// faction's present/dead/wounded roster. Immutable snapshot built from final session state.
public sealed record FactionBattleSummary
{
  public required Faction Faction { get; init; }
  public required IReadOnlySet<Combatant> CombatantsPresent { get; init; }
  public required IReadOnlyDictionary<Combatant, List<Combatant>> DefeatedPerCombatant { get; init; }
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
    if (session.Outcome.IsNone)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over."));

    return Right(session.GetFactionSummary(faction));
  }
}

// The side whose turn it currently is. Total: the scheduler establishes an active side at
// construction and retains the last one after completion.
public sealed class GetActiveSideQuery : IBattleSessionQuery<Faction>
{
  public Faction Execute(BattleSession session) => session.ActiveSide;
}

// The battle's single authoritative completion value: None while the battle is live.
public sealed class GetBattleOutcomeQuery : IBattleSessionQuery<Option<BattleOutcome>>
{
  public Option<BattleOutcome> Execute(BattleSession session) => session.Outcome;
}
