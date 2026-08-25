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

/// <summary>Builds the final battle-result snapshot once the session has ended.</summary>
public sealed class GetBattleResultQuery : IBattleSessionQuery<Either<BattleQueryFailure, BattleResult>>
{
  public Either<BattleQueryFailure, BattleResult> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Ended)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over."));

    var factions = new SysColGeneric.Dictionary<Faction, FactionResultCounts>();
    foreach (Faction faction in session.GlobalFactionTurnOrder)
    {
      int spawned = 0;
      int killed = 0;

      foreach (BattleUnitState unit in session.AliveUnits.AsValueEnumerable())
      {
        if (unit.Side == faction)
          spawned++;
      }

      foreach (BattleUnitState unit in session.DeadUnits.AsValueEnumerable())
      {
        if (unit.Side != faction)
          continue;

        spawned++;
        killed++;
      }

      factions[faction] = new FactionResultCounts(spawned, killed);
    }

    int objectsInteracted = 0;
    int objectsExpired = 0;
    foreach (BattleObjectState obj in session.Objects.AsValueEnumerable())
    {
      if (obj.Status == Some(ObjectStatus.Interacted))
        objectsInteracted++;
      else if (obj.Status == Some(ObjectStatus.Expired))
        objectsExpired++;
    }

    return Right(new BattleResult(
      session.Outcome.Match(
        value => value,
        () => throw new InvalidOperationException("Battle session ended without an outcome.")),
      session.TurnNumber,
      factions,
      objectsInteracted,
      objectsExpired));
  }
}
