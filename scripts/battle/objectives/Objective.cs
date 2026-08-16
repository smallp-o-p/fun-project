using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Runtime objective instance: behavior + parameters in code (constructor takes its authored
// ObjectiveData subclass), display text + outcome directives on the data. Objectives are
// faction-agnostic predicates; the router supplies the evaluating faction to Check. State flips
// (Ongoing -> Passed/Failed) only through the session's Record doors, driven by ObjectiveSystem
// when an observed event makes Check return a flip; an objective is evaluated from the NEXT
// event after it was added.
public abstract class Objective
{
  public ObjectiveData Data { get; }

  public ObjectiveResult State { get; internal set; } = ObjectiveResult.Ongoing;

  protected Objective(ObjectiveData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    Data = data;
  }

  // Event-key types this objective observes: concrete event records and/or tag interfaces
  // (e.g. typeof(UnitKilledBattleEvent), typeof(IUnitBattleEvent)).
  public abstract IReadOnlyCollection<Type> ObservedEventKeys { get; }

  // Pure query over the session plus the just-committed event: Ongoing, or the flip.
  public abstract ObjectiveResult Check(Faction owner, BattleEvent battleEvent, BattleSession session);
}
