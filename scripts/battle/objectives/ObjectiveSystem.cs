using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Turn-end After hook: evaluates every faction's operation/objective status. Unlike the
/// status-effect and armor-regen passes (which are scoped to the faction whose turn just
/// ended), operation evaluation is global — an operation can become complete/failed because
/// of another faction's turn (e.g. a reaction kill during the enemy turn completing the
/// player's eliminate-all objective); see <see cref="BattleSession.EvaluateAllOperationsAtTurnEnd"/>.
/// Registered at priority +100 so it runs after the turn-end tick hooks, against
/// post-tick/post-regen state.
/// </summary>
public sealed class ObjectiveSystem : BattleHook<TurnEndedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent turnEnded)
  {
    context.Session.EvaluateAllOperationsAtTurnEnd();
    return [];
  }
}
