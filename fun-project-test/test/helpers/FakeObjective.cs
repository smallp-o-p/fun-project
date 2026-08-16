#nullable enable

using FunProject.Battle;
using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

// Test-double objective: flips based on settable flags, counts routed checks, and observes
// a configurable event key (UnitKilledBattleEvent by default).
public sealed class FakeObjective : Objective
{
  public bool Complete { get; set; }
  public bool Failed { get; set; }
  public Type? Observe { get; set; }
  public int CheckCount { get; private set; }

  public FakeObjective() : this(new FakeObjectiveData())
  {
  }

  // Convenience ctor for directive-carrying tests: directives live on the data.
  public FakeObjective(ObjectiveData data) : base(data)
  {
    if (data is not FakeObjectiveData fakeData)
      return;

    Complete = fakeData.Complete;
    Failed = fakeData.Failed;
    Observe = fakeData.Observe;
  }

  public override IReadOnlyCollection<Type> ObservedEventKeys =>
    Observe is null ? [typeof(UnitKilledBattleEvent)] : [Observe];

  public override ObjectiveResult Check(Faction _, BattleEvent battleEvent, BattleSession session)
  {
    CheckCount++;
    if (Failed)
      return ObjectiveResult.Failed;
    return Complete ? ObjectiveResult.Passed : ObjectiveResult.Ongoing;
  }
}
