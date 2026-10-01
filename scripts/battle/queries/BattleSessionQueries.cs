using FunProject.Combatants;

namespace FunProject.Battle;

// The side whose turn it currently is, with its round number: Some while the battle runs,
// None once completion installed. Total — it never throws because of the lifecycle.
public sealed class GetCurrentTurnQuery : IBattleSessionQuery<Option<BattleTurn>>
{
  public Option<BattleTurn> Execute(BattleReadContext context) => context.CurrentTurn;
}
