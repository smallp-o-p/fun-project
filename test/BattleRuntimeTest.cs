using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeTest
{
  [TestCase(0, TestName = "Query executes against the session")]
  [TestCase(1, TestName = "ExecuteAction delegates to the executor and the runtime republishes session events")]
  public void RuntimeForwardsQueriesActionsAndEvents(int z)
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    battle.Start();
    var runtime = battle.Runtime;
    battle.ClearEvents();
    var cell = new Vector3I(1, 0, z);
    // Runtime-only driving (no conveniences): the fixture's runtime owns this session's
    // only executor.
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction), battle.Board.At(cell)));

    Option<BattleUnitState> queried = runtime.Query(new GetUnitAtTile(battle.Board.At(cell)));

    Assert.Equal(battle.UnitAt(cell), queried.RequireSome());
    Assert.True(battle.PositionOf(queried.RequireSome()).IsSome);
    Assert.True(battle.Events.EventsOf<UnitAddedBattleEvent>().Length > 0);
  }

  [TestCase(false, TestName = "RegisterHook affects runtime action execution")]
  [TestCase(true, TestName = "RegisterHook event shape affects runtime action execution")]
  public void RuntimeRegistrationShapesExecution(bool eventShape)
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = battle.Runtime;
    var unit = battle.Unit;
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    string label = eventShape ? "runtime_shape_reaction" : "runtime_reaction";
    var hook = new RuntimeRecordingHook(label, targetPosition, log);
    if (eventShape)
      runtime.RegisterHook<IPositionedBattleEvent>(hook);
    else
      runtime.RegisterHook<UnitMovedBattleEvent>(hook);

    runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.Board.At(targetPosition)]));

    Assert.Equal(1, log.Count);
    Assert.Equal(label, log[0]);
  }

  [TestCase(TestName = "Disposed runtime no longer republishes session events")]
  public void DisposedRuntimeNoLongerRepublishesSessionEvents()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    battle.Start();
    var runtime = battle.Runtime;
    var observed = new List<BattleEvent>();
    runtime.BattleEventCommitted += observed.Add;

    // Disposal closes the only execution scope: no further event can be committed, and the
    // disposed runtime must not re-raise anything.
    runtime.Dispose();

    Assert.Equal(0, observed.Count);
    Assert.Throws<ObjectDisposedException>(() =>
      runtime.ExecuteAction(BattleAction.SpawnUnit(
        TestData.MakeCombatant("Alpha", faction), battle.Board.At(1, 0, 1))));
    runtime.BattleEventCommitted -= observed.Add;
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    battle.Start();
    var runtime = battle.Runtime;
    var hook = new RuntimeRecordingHook("runtime_reaction", new Vector3I(1, 0, 0), []);

    runtime.Dispose();

    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetFactionAliveUnits(faction)));
    Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction),
      battle.Board.At(1, 0, 1))));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<UnitMovedBattleEvent>(hook));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<IPositionedBattleEvent>(
      new RuntimeRecordingHook("runtime_shape_reaction", new Vector3I(1, 0, 0), [])));
    Assert.Throws<ObjectDisposedException>(() => runtime.UnregisterHook<UnitMovedBattleEvent>(hook));

    // Disposal is idempotent; the second call must not mask a broken first cleanup above.
    runtime.Dispose();
  }

  // The runtime's re-raise fires before its executor's hook reactions, so scene subscribers
  // on Runtime.BattleEventCommitted see the cause before any hook-born follow-up — the
  // order this test pins.
  [TestCase(TestName = "Scene subscribers observe a cause event before hook-born follow-up events")]
  public void SceneSubscribersObserveCauseBeforeHookFollowUps()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10);
    var runtime = battle.Runtime;
    var unit = battle.Unit;

    var observed = new List<BattleEvent>();
    runtime.BattleEventCommitted += observed.Add;
    runtime.RegisterHook<UnitMovedBattleEvent>(new DamageMoverOnMovedHook());

    runtime.ExecuteAction(BattleAction.MoveUnit(
      battle.Alive(unit), [battle.Board.At(1, 0, 0)]));

    int causeIndex = observed.EventIndex<UnitMovedBattleEvent>();
    int followUpIndex = observed.EventIndex<UnitDamagedBattleEvent>();
    Assert.True(causeIndex >= 0);
    Assert.True(followUpIndex > causeIndex);
  }

  [TestCase(TestName = "Hook interrupts with no executor action in flight throw")]
  public void HookInterruptsWithoutInFlightActionThrow()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;
    battle.RegisterHook<UnitMovedBattleEvent>(new DamageMoverOnMovedHook());

    var from = battle.PositionOf(unit).RequireSome();
    var to = battle.Board.At(new Vector3I(1, 0, 0));
    Assert.Throws<InvalidOperationException>(() => battle.Session.MoveUnit(unit, from, to));
  }

  private sealed partial class RuntimeRecordingHook : BattleHook
  {
    private readonly string _message;
    private readonly Vector3I _targetPosition;
    private readonly List<string> _log;

    public RuntimeRecordingHook(string message, Vector3I targetPosition, List<string> log)
    {
      _message = message;
      _targetPosition = targetPosition;
      _log = log;
    }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not UnitMovedBattleEvent movedEvent
        || movedEvent.Position.Raw != _targetPosition)
        return [];

      _log.Add(_message);
      return [];
    }
  }

  private sealed class DamageMoverOnMovedHook : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => [BattleAction.ApplyDamage(context.Read.State.TryGetAlive(evt.Unit).RequireSome(), 3)];
  }
}
