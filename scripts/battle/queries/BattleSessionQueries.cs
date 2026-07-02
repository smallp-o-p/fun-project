using System;
using FunProject.Combatants;

namespace FunProject.Battle;

public sealed class GetFactionEndOfBattleSummary(Faction faction)
  : BattleSessionQuery<BattleSession.FactionBattleSummary>
{
  internal override Either<BattleQueryFailure, BattleSession.FactionBattleSummary> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Ended)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Battle session isn't over."));

    return Right(session.GetFactionSummary(faction));
  }
}

public class BattleSessionQueries
{
  
}