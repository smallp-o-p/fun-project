using FunProject.Combatants;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace FunProject.Battle;

// Objective-owned history operations shared by the running router and preparation: ordered
// candidate snapshots, ongoing→passed/failed flip recording, and follow-up addition. These
// work against the retained state while running and after settlement alike; lifecycle
// policies (terminal directives, backstops) stay at the callers.
internal static class ObjectiveHistory
{
  // Snapshot before iterating: a directive may add objectives mid-dispatch, and a
  // follow-up must be evaluated from the next event, never the one that queued it.
  // The faction-order × objective-add-order traversal is the determinism contract.
  internal static (Faction Owner, Objective Objective)[] SnapshotCandidates(BattleState state, BattleEvent battleEvent)
  {
    FrozenSet<Type> eventKeys = battleEvent.EventKeys;
    List<(Faction Owner, Objective Objective)> candidates = [];

    foreach (Faction faction in state.Factions)
      foreach (Objective objective in state.GetObjectives(faction))
        if (objective.State == ObjectiveResult.Ongoing
            && objective.ObservedEventKeys.AsValueEnumerable().Any(eventKeys.Contains))
          candidates.Add((faction, objective));

    return [.. candidates];
  }

  // State first, then the flip event, so observers see the flip before anything it causes.
  internal static void RecordFlip(BattleState state, Faction owner, Objective objective, bool passed)
  {
    if (objective.State != ObjectiveResult.Ongoing)
      throw new InvalidOperationException($"Objective {objective.Data.Name} is already {objective.State}.");

    objective.State = passed ? ObjectiveResult.Passed : ObjectiveResult.Failed;
    state.RaiseEvents(passed
      ? new ObjectiveCompletedBattleEvent(owner, objective)
      : new ObjectiveFailedBattleEvent(owner, objective));
  }

  // Adds the objective (the state registers a first-seen faction for turns) and raises
  // ObjectiveAdded. Instance-bound follow-ups become visible from the next event.
  internal static void AddFollowUp(BattleState state, Faction faction, Objective objective)
  {
    state.AddObjective(faction, objective);
    state.RaiseEvents(new ObjectiveAddedBattleEvent(faction, objective));
  }
}
