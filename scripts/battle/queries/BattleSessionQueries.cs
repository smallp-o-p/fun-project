using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

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
}

public sealed class GetFactionEndOfBattleSummary(Faction faction)
  : IBattleSessionQuery<Either<BattleQueryFailure, FactionBattleSummary>>
{
  public Either<BattleQueryFailure, FactionBattleSummary> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Ended)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over."));

    return Right(session.GetFactionSummary(faction));
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
