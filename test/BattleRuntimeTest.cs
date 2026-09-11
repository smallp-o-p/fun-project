using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeTest
{
  [TestCase(TestName = "Query executes query against session")]
  public void QueryExecutesQueryAgainstSession()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    var runtime = battle.Runtime;
    // Runtime-only driving (no conveniences): the fixture's runtime owns this session's
    // only executor.
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction), battle.Board.At(1, 0, 0)));

    Option<BattleUnitState> result = runtime.Query(new GetUnitAtTile(battle.Board.At(1, 0, 0)));

    Assert.Equal(battle.Session.GetUnitAt(battle.Board.At(1, 0, 0)).RequireSome(), result.RequireSome());
  }

  [TestCase(TestName = "ExecuteAction delegates to action executor")]
  public void ExecuteActionDelegatesToActionExecutor()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    var runtime = battle.Runtime;

    runtime.ExecuteAction(BattleAction.SpawnUnit(TestData.MakeCombatant("Alpha", faction), battle.Board.At(1, 0, 1)));

    BattleUnitState unit = battle.Session.GetUnitAt(battle.Board.At(1, 0, 1)).RequireSome();
    Assert.True(battle.Session.GetUnitPosition(unit).IsSome);
  }

  [TestCase(TestName = "Runtime republishes session events")]
  public void RuntimeRepublishesSessionEvents()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    var runtime = battle.Runtime;

    battle.ClearEvents();
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction),
      battle.Board.At(1, 0, 1)));

    Assert.True(battle.Events.EventsOf<UnitAddedBattleEvent>().Length > 0);
  }

  [TestCase(TestName = "RegisterHook affects runtime action execution")]
  public void RegisterHookAffectsRuntimeActionExecution()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = battle.Runtime;
    var unit = battle.Unit;
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterHook<UnitMovedBattleEvent>(new RuntimeRecordingHook("runtime_reaction", targetPosition, log));

    runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.Board.At(targetPosition)]));

    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_reaction", log[0]);
  }

  [TestCase(TestName = "RegisterHook event shape affects runtime action execution")]
  public void RegisterHookEventShapeAffectsRuntimeActionExecution()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = battle.Runtime;
    var unit = battle.Unit;
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterHook<IPositionedBattleEvent>(
      new RuntimeRecordingHook("runtime_shape_reaction", targetPosition, log));

    runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.Board.At(targetPosition)]));

    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_shape_reaction", log[0]);
  }

  [TestCase(TestName = "Disposed runtime no longer republishes session events")]
  public void DisposedRuntimeNoLongerRepublishesSessionEvents()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    var runtime = battle.Runtime;
    var observed = new List<BattleEvent>();
    runtime.BattleEventCommitted += observed.Add;

    runtime.Dispose();
    // A retained replacement executor is safe here: the runtime's own executor was
    // disposed with it, so this is still the session's only live executor, and it commits
    // an event the disposed runtime must not re-raise.
    using var replacement = new BattleActionExecutor(battle.Session);
    replacement.Submit(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction), battle.Board.At(1, 0, 1)));

    Assert.Equal(0, observed.Count);
    runtime.BattleEventCommitted -= observed.Add;
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
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
  }

  [TestCase(TestName = "Dispose is idempotent")]
  public void DisposeIsIdempotent()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);

    battle.Runtime.Dispose();
    battle.Runtime.Dispose();
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

    var from = battle.Session.GetUnitPosition(unit).RequireSome();
    var to = battle.Board.At(new Vector3I(1, 0, 0));
    Assert.Throws<InvalidOperationException>(() => battle.Session.MoveUnit(unit, from, to));
  }

  [TestCase(TestName = "Disposing the runtime disposes its executor")]
  public void DisposingRuntimeDisposesExecutor()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    battle.Runtime.Dispose();
    Assert.Throws<ObjectDisposedException>(() =>
      battle.Runtime.ExecuteAction(BattleAction.SpawnUnit(
        TestData.MakeCombatant("Alpha", faction), battle.Board.At(0, 0, 0))));
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
      => [BattleAction.ApplyDamage(context.Session.TryGetAlive(evt.Unit).RequireSome(), 3)];
  }
}
