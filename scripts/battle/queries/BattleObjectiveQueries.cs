using FunProject.Combatants;
using System;

namespace FunProject.Battle;

public sealed class GetOperationForFaction : BattleSessionQuery<Operation>
{
  public Faction Side { get; }

  public GetOperationForFaction(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, Operation> Execute(BattleSession session)
  {
    return session.GetOperation(Side).Match(
      Succeed,
      () => Fail(BattleQueryFailureReason.InvalidBattleState, $"Faction {Side.Name} has no operation in this battle."));
  }
}
