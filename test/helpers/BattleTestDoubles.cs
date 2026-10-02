#nullable disable warnings
using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

// Collects every event it receives; register with executor/runtime RegisterHook<TEventKey>.
internal sealed class RecordingHook : BattleHook
{
  public List<BattleEvent> Received { get; } = [];

  public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
  {
    Received.Add(battleEvent);
    return [];
  }
}

// Spends every listed unit's remaining AP during the session-start dispatch: a shared
// stand-in for pre-refresh state that a later normalization pass must repair.
internal sealed class SpendActionPointsHook(IReadOnlyList<BattleUnitState> units) : BattleHook<SessionStartedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, SessionStartedBattleEvent evt)
  {
    foreach (BattleUnitState unit in units)
      unit.TrySpendActionPoints(unit.CurrentActionPoints);
    return [];
  }
}

// Setup-time declared-system double: records the runtime it registered with and forwards to
// a test-supplied callback, so lifecycle tests can hook in during startup.
internal sealed partial class SetupSystemData : BattleTypeSystemData
{
  public Action<BattleRuntime> OnRegister { get; init; } = _ => { };
  public BattleRuntime? RegisteredRuntime { get; private set; }

  public override void Register(BattleRuntime runtime)
  {
    RegisteredRuntime = runtime;
    OnRegister(runtime);
  }
}

// Throws the supplied failure when the matching event type commits; startup-fault tests use
// it to prove the original exception survives and the runtime is disposed.
internal sealed class ThrowOnSetupEvent(Type eventType, Exception failure) : BattleHook
{
  public override IReadOnlyList<BattleAction> OnEvent(
    HookContext context, BattleEvent battleEvent)
  {
    if (battleEvent.GetType() == eventType)
      throw failure;
    return [];
  }
}

// Logs a message when it sees a positioned event on the target tile; counts every evaluation.
internal sealed partial class PositionRecordingHook : BattleHook
{
  private readonly Vector3I _position;
  private readonly List<string> _log;
  private readonly string _message;

  public int EvaluateCallCount { get; private set; }

  public PositionRecordingHook(Vector3I position, List<string> log, string message)
  {
    _position = position;
    _log = log;
    _message = message;
  }

  public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
  {
    EvaluateCallCount++;
    if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
      return [];

    _log.Add(_message);
    return [];
  }
}

internal sealed class AlwaysHitCalculator : IHitChanceCalculator
{
  public HitChanceBreakdown Calculate(AttackContext context) => new(100, []);
}

// Test-double objective: flips based on settable flags, counts routed checks, and observes
// a configurable event key (UnitKilledBattleEvent by default).
public sealed class FakeObjective : Objective
{
  public bool Complete { get; set; }
  public bool Failed { get; set; }
  public Type Observe { get; set; }
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

  public override ObjectiveResult Check(Faction _, BattleEvent battleEvent, BattleReadContext context)
  {
    CheckCount++;
    if (Failed)
      return ObjectiveResult.Failed;
    return Complete ? ObjectiveResult.Passed : ObjectiveResult.Ongoing;
  }
}
