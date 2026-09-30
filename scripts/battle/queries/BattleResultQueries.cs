using FunProject.Combatants;
using System;

namespace FunProject.Battle;

/// <summary>Per-faction unit counts captured in an end-of-battle result.</summary>
public sealed record FactionResultCounts(int Spawned, int Killed);

/// <summary>Battle outcome snapshot for results/presentation code.</summary>
public sealed record BattleResult(
  BattleOutcome Outcome,
  int TurnCount,
  SysColGeneric.IReadOnlyDictionary<Faction, FactionResultCounts> Factions,
  int ObjectsInteracted,
  int ObjectsExpired);

/// <summary>Battle outcome snapshot for results/presentation code; reads the session's
/// stored completion.</summary>
public sealed class GetBattleResultQuery : IBattleSessionQuery<Either<BattleQueryFailure, BattleResult>>
{
  public Either<BattleQueryFailure, BattleResult> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Ended)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over."));

    return session.Completed.Match(
      completed => Right<BattleQueryFailure, BattleResult>(new BattleResult(
        completed.Outcome,
        completed.TurnCount,
        completed.Factions,
        completed.ObjectsInteracted,
        completed.ObjectsExpired)),
      () => Left<BattleQueryFailure, BattleResult>(new BattleQueryFailure(
        BattleQueryFailureReason.InvalidBattleState, "Battle session ended without a completed report.")));
  }
}

// None while the battle runs; Some once the terminal boundary has captured the frozen report.
public sealed class GetCompletedBattleQuery : IBattleSessionQuery<Option<CompletedBattle>>
{
  public Option<CompletedBattle> Execute(BattleSession session) => session.Completed;
}
