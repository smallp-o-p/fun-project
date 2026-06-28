namespace FunProject.Battle;

/// <summary>
/// Turn-end bookkeeping listener: evaluates every faction's operation/objective status. Unlike
/// the status-effect and armor-regen passes (which are scoped to the faction whose turn just
/// ended), operation evaluation is global — an operation can become complete/failed because of
/// another faction's turn (e.g. a reaction kill during the enemy turn completing the player's
/// eliminate-all objective); see <see cref="BattleSession.EvaluateAllOperationsAtTurnEnd"/>.
/// Registered Low so it runs after all upkeep, against post-tick/post-regen state. Mutates
/// state and raises follow-up events only; it never submits executor actions.
/// </summary>
public sealed class ObjectiveSystem : BattleEventListener<TurnEndedBattleEvent>
{
  protected override void OnEvent(BattleSession session, TurnEndedBattleEvent turnEnded)
  {
    session.EvaluateAllOperationsAtTurnEnd();
  }
}
