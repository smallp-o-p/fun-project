using FunProject.Combatants;

namespace FunProject.Battle;

public sealed class GetOperationForFaction(Faction side) : IBattleSessionQuery<Option<Operation>>
{
  public Option<Operation> Execute(BattleSession session)
  {
    return session.GetOperation(side);
  }
}
