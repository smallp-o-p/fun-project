using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// The objective router: the executor registers this plain default system once under
/// <see cref="BattleEventTag"/> at priority +100, so it receives every committed event.
/// Objectives filter by their declared <see cref="Objective.ObservedEventKeys"/> inside
/// the router. The router supplies each objective's owning faction to <see cref="Objective.Check"/>.
/// Dispatch keys are owned by the event itself (per-closed-type statics behind
/// <see cref="BattleEvent{TSelf}.EventKeys"/>).
/// Candidates are snapshotted for each event in global faction order, then objective add
/// order; this is the determinism contract. Follow-ups added by a directive are therefore
/// visible from the next event, not retroactively in the event that queued them. On a flip
/// the outcome is recorded FIRST (state + events, so observers see the flip before anything
/// it causes), then the directive runs: end the battle, queue instance-bound follow-ups, or
/// nothing (null). Flip recording and follow-up queueing are history operations that work
/// against the retained state after settlement; ending the battle delegates to the running
/// receiver for the current step, so a directive observed by a completed event context can
/// never reopen combat.
/// </summary>
public sealed class ObjectiveSystem : BattleHook
{
  public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
  {
    BattleReadContext read = context.Read;

    // The player-wipe backstop owns this ending; already-flipped objectives stay in
    // history, but the final disabling does not double-resolve through authored directives.
    if (read.RunningSession.Match(
          Some: session => battleEvent is (UnitKilledBattleEvent or UnitUnconsciousBattleEvent) and IUnitBattleEvent unitEvent
            && session.PlayerFaction.Match(
              Some: player => unitEvent.Unit.Side == player && !session.State.HasConsciousUnits(player),
              None: () => false),
          None: () => false))
      return [];

    foreach (var (owner, objective) in ObjectiveHistory.SnapshotCandidates(read.State, battleEvent))
    {
      ObjectiveResult result = objective.Check(owner, battleEvent, read);
      if (result == ObjectiveResult.Ongoing)
        continue;

      ObjectiveHistory.RecordFlip(read.State, owner, objective, result == ObjectiveResult.Passed);
      ObjectiveDirectiveData? directive = result == ObjectiveResult.Passed
        ? objective.Data.OnComplete
        : objective.Data.OnFail;

      if (ApplyDirective(read, owner, directive))
        break;
    }

    return [];
  }

  // Returns true when the directive was terminal (the battle is ending or already ended),
  // which stops the event's candidate traversal even before settlement.
  private static bool ApplyDirective(BattleReadContext read, Faction owner, ObjectiveDirectiveData? directive)
  {
    switch (directive)
    {
      case null:
        return false;
      case EndBattleDirectiveData endBattle:
        // Causal and parent events still update objective history, but the player-wipe
        // backstop owns the outcome once no conscious player forces remain.
        bool playerWiped = read.RunningSession.Match(
          Some: session => session.PlayerFaction.Match(
            Some: player => !session.State.HasConsciousUnits(player),
            None: () => false),
          None: () => false);
        if (playerWiped)
          return false;
        // A completed event context carries no receiver: history records, combat stays closed.
        read.RunningSession.IfSome(session => session.RequestEnd(endBattle.Outcome));
        return true;
      case QueueDirectiveData queue:
        foreach (ObjectiveData followUp in queue.FollowUps)
          ObjectiveHistory.AddFollowUp(read.State, owner, followUp.Instantiate());
        return false;
      default:
        throw new InvalidOperationException(
          $"Unknown objective directive type {directive.GetType().Name}.");
    }
  }
}
