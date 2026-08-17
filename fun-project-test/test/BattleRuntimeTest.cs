using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeTest
{
  [TestCase(TestName = "Query executes query against session")]
  public void QueryExecutesQueryAgainstSession()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = RuntimeFor(session);
    // Runtime-only driving (no session-taking helpers): the runtime's executor is this
    // session's only one.
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(1, 0, 0)));

    Option<BattleUnitState> result = runtime.Query(new GetUnitAtTile(session.Board.At(1, 0, 0)));

    Assert.Equal(session.GetUnitAt(session.Board.At(1, 0, 0)).RequireSome(), result.RequireSome());
  }

  [TestCase(TestName = "ExecuteAction delegates to action executor")]
  public void ExecuteActionDelegatesToActionExecutor()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = RuntimeFor(session);

    runtime.ExecuteAction(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(1, 0, 1)))
      ;

    BattleUnitState unit = session.GetUnitAt(session.Board.At(1, 0, 1)).RequireSome();
    Assert.True(session.GetUnitPosition(unit).IsSome);
  }

  [TestCase(TestName = "Runtime republishes session events")]
  public void RuntimeRepublishesSessionEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = RuntimeFor(session);
    var recorder = new BattleEventRecorder(runtime);

    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      session.Board.At(1, 0, 1)));

    Assert.True(recorder.OfType<UnitAddedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "RegisterHook affects runtime action execution")]
  public void RegisterHookAffectsRuntimeActionExecution()
  {
    var (session, runtime, unit) = StartSoloRuntime(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterHook<UnitMovedBattleEvent>(new RuntimeRecordingHook("runtime_reaction", targetPosition, log));

    runtime.ExecuteAction(BattleAction.MoveUnit(unit, [session.Board.At(targetPosition)]))
      ;

    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_reaction", log[0]);
  }

  [TestCase(TestName = "RegisterHook event shape affects runtime action execution")]
  public void RegisterHookEventShapeAffectsRuntimeActionExecution()
  {
    var (session, runtime, unit) = StartSoloRuntime(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterHook<IPositionedBattleEvent>(
      new RuntimeRecordingHook("runtime_shape_reaction", targetPosition, log));

    runtime.ExecuteAction(BattleAction.MoveUnit(unit, [session.Board.At(targetPosition)]))
      ;

    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_shape_reaction", log[0]);
  }

  [TestCase(TestName = "Disposed runtime no longer republishes session events")]
  public void DisposedRuntimeNoLongerRepublishesSessionEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var recorder = new BattleEventRecorder(runtime);

    runtime.Dispose();
    // A fresh executor is safe here: the runtime's own executor was disposed with it, so
    // this is still the session's only LIVE executor, and it commits an event the disposed
    // runtime must not re-raise.
    new BattleActionExecutor(session)
      .Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(1, 0, 1)))
      ;

    Assert.Equal(0, recorder.All.Count);
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = RuntimeFor(session);
    var hook = new RuntimeRecordingHook("runtime_reaction", new Vector3I(1, 0, 0), []);

    runtime.Dispose();

    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetFactionAliveUnits(faction)));
    Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      session.Board.At(1, 0, 1))));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<UnitMovedBattleEvent>(hook));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<IPositionedBattleEvent>(
      new RuntimeRecordingHook("runtime_shape_reaction", new Vector3I(1, 0, 0), [])));
    Assert.Throws<ObjectDisposedException>(() => runtime.UnregisterHook<UnitMovedBattleEvent>(hook));
  }

  [TestCase(TestName = "Dispose is idempotent")]
  public void DisposeIsIdempotent()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = RuntimeFor(session);

    runtime.Dispose();
    runtime.Dispose();
  }

  // Deliberately uses NO session-taking helpers (they would mint the cached executor and
  // produce a second executor alongside the runtime's own): spawn and start go through
  // runtime.ExecuteAction on the runtime's public-ctor executor, whose hook reactions fire
  // AFTER this runtime's re-raise — the order this test pins.
  [TestCase(TestName = "Scene subscribers observe a cause event before hook-born follow-up events")]
  public void SceneSubscribersObserveCauseBeforeHookFollowUps()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    EnsureEveryFactionHasObjective(session);
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction, health: 10), session.Board.At(0, 0, 0)))
      ;
    runtime.ExecuteAction(BattleAction.StartBattle());

    var observed = new List<BattleEvent>();
    runtime.BattleEventCommitted += observed.Add;
    runtime.RegisterHook<UnitMovedBattleEvent>(new DamageMoverOnMovedHook());

    runtime.ExecuteAction(BattleAction.MoveUnit(
      session.TryGetAlive(session.GetUnitAt(session.Board.At(0, 0, 0)).RequireSome()).RequireSome(),
      [session.Board.At(1, 0, 0)]));

    int causeIndex = observed.FindIndex(e => e is UnitMovedBattleEvent);
    int followUpIndex = observed.FindIndex(e => e is UnitDamagedBattleEvent);
    Assert.True(causeIndex >= 0);
    Assert.True(followUpIndex > causeIndex);
  }

  [TestCase(TestName = "Hook interrupts with no executor action in flight throw")]
  public void HookInterruptsWithoutInFlightActionThrow()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    executor.RegisterHook<UnitMovedBattleEvent>(new DamageMoverOnMovedHook());

    var from = session.GetUnitPosition(unit.State).RequireSome();
    var to = session.Board.At(new Vector3I(1, 0, 0));
    Assert.Throws<InvalidOperationException>(() => session.MoveUnit(unit.State, from, to));
  }

  [TestCase(TestName = "Disposing the runtime disposes its executor")]
  public void DisposingRuntimeDisposesExecutor()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    runtime.Dispose();
    Assert.Throws<ObjectDisposedException>(() =>
      runtime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(0, 0, 0))));
  }

  // Solo battle driven entirely through the runtime (spawn + start via ExecuteAction), so
  // the runtime's executor is the session's only one — for tests that exercise runtime
  // behavior and must not mix in the ExecutorFor session helpers.
  private static (BattleSession session, BattleRuntime runtime, AliveUnit unit) StartSoloRuntime(
    Vector3I dimensions,
    Vector3I unitPosition,
    int actionPoints = 4)
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(dimensions, [faction]);
    var runtime = RuntimeFor(session);
    EnsureEveryFactionHasObjective(session);
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: actionPoints),
      session.Board.At(unitPosition)));
    runtime.ExecuteAction(BattleAction.StartBattle());
    return (session, runtime, session.TryGetAlive(session.GetUnitAt(session.Board.At(unitPosition)).RequireSome()).RequireSome());
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
