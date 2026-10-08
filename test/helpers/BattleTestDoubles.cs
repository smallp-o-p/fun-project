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

// Setup-time declared-system double: records the hook-registration capability it received
// and forwards to a test-supplied callback, so lifecycle tests can hook in during startup.
internal sealed partial class SetupSystemData : BattleTypeSystemData
{
  public Action<BattleHookRegistration> OnRegister { get; init; } = _ => { };
  public BattleHookRegistration? RegisteredHooks { get; private set; }

  public override void Register(BattleHookRegistration hooks)
  {
    RegisteredHooks = hooks;
    OnRegister(hooks);
  }
}

// Fixture-owned declared recording system: BattleFixture appends it to the setup before the
// factory call, so its catch-all hook is registered before factory startup and records every
// committed event — including all startup events — into the fixture's assertion window. It
// also captures the session from the first event for the fixture's test-only Session handle
// and indexes unit-added records by (combatant identity, event placement point) for
// authored-role resolution — the first record per key wins, so a later startup spawn
// reusing an authored combatant/cell (a kill-and-replace reinforcement) cannot overwrite
// the authored raw unit. The hook lives in the registered executor, so it is tied to that
// executor: a replacement executor over the same session never reaches it.
internal sealed partial class FixtureRecordingSystem : BattleTypeSystemData
{
  private readonly List<BattleEvent> _events;

  public Option<BattleSession> Session { get; private set; } = None;
  public Dictionary<(Combatant, Vector3I), BattleUnitState> AddedUnits { get; } = [];

  public FixtureRecordingSystem(List<BattleEvent> events) => _events = events;

  public override void Register(BattleHookRegistration hooks) =>
    hooks.RegisterHook<IBattleEventTag>(new Recorder(this), int.MinValue);

  private sealed class Recorder(FixtureRecordingSystem owner) : BattleHook
  {
    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (owner.Session.IsNone)
        owner.Session = Some(context.Session);
      if (battleEvent is UnitAddedBattleEvent added)
        owner.AddedUnits.TryAdd((added.Unit.Combatant, added.Position.Raw), added.Unit);
      owner._events.Add(battleEvent);
      return [];
    }
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

  public override ObjectiveResult Check(Faction owner, BattleEvent battleEvent, BattleSession session)
  {
    CheckCount++;
    if (Failed)
      return ObjectiveResult.Failed;
    return Complete ? ObjectiveResult.Passed : ObjectiveResult.Ongoing;
  }
}
