using FunProject.Combatants;
using System;
using System.Collections.Frozen;
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
/// nothing (null). Never returns interrupts; if the battle has already ended, directives rely
/// on <see cref="BattleSession.EndBattle"/> being idempotent so late flips can still be recorded.
/// </summary>
public sealed class ObjectiveSystem : BattleHook
{
  private readonly BattleSession _session;

  public ObjectiveSystem(BattleSession session)
  {
    _session = session;
  }

  public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
  {
    // The player-wipe backstop owns this ending; already-flipped objectives stay in
    // history, but the final disabling does not double-resolve through authored directives.
    if (battleEvent is (UnitKilledBattleEvent or UnitUnconsciousBattleEvent) and IUnitBattleEvent unitEvent
        && context.Session.PlayerFaction.Match(
          Some: player => unitEvent.Unit.Side == player && !context.Session.HasConsciousUnits(player),
          None: () => false))
      return [];

    foreach (var (owner, objective) in InterestedIn(battleEvent))
    {
      ObjectiveResult result = objective.Check(owner, battleEvent, _session);
      if (result == ObjectiveResult.Ongoing)
        continue;

      ObjectiveDirectiveData? directive;
      if (result == ObjectiveResult.Passed)
      {
        _session.RecordObjectiveCompleted(owner, objective);
        directive = objective.Data.OnComplete;
      }
      else
      {
        _session.RecordObjectiveFailed(owner, objective);
        directive = objective.Data.OnFail;
      }

      ApplyDirective(owner, directive);
      if (_session.Phase == BattlePhase.Ended)
        break;
    }

    return [];
  }

  // Snapshot before iterating: a directive may add objectives mid-dispatch, and a
  // follow-up must be evaluated from the next event, never the one that queued it.
  // The faction-order × objective-add-order traversal is the determinism contract.
  private (Faction Owner, Objective Objective)[] InterestedIn(BattleEvent battleEvent)
  {
    FrozenSet<Type> eventKeys = battleEvent.EventKeys;
    List<(Faction Owner, Objective Objective)> candidates = [];

    foreach (Faction faction in _session.GlobalFactionTurnOrder)
      foreach (Objective objective in _session.GetObjectives(faction))
        if (objective.State == ObjectiveResult.Ongoing
            && objective.ObservedEventKeys.AsValueEnumerable().Any(eventKeys.Contains))
          candidates.Add((faction, objective));

    return [.. candidates];
  }

  private void ApplyDirective(Faction owner, ObjectiveDirectiveData? directive)
  {
    switch (directive)
    {
      case null:
        return;
      case EndBattleDirectiveData endBattle:
        // Causal and parent events still update objective history, but the player-wipe
        // backstop owns the outcome once no conscious player forces remain.
        if (_session.PlayerFaction.Match(
              Some: player => !_session.HasConsciousUnits(player),
              None: () => false))
          return;
        _session.EndBattle(endBattle.Outcome);
        return;
      case QueueDirectiveData queue:
        foreach (ObjectiveData followUp in queue.FollowUps)
        {
          Objective followUpRuntime = followUp.Instantiate();
          _session.AddObjective(owner, followUpRuntime);
        }
        return;
      default:
        throw new InvalidOperationException(
          $"Unknown objective directive type {directive.GetType().Name}.");
    }
  }
}
