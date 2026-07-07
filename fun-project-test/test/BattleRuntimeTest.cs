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
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 0));
    var runtime = new BattleRuntime(session);

    Option<BattleUnitState> result = runtime.Query(new GetUnitAtTile(session.Board.At(1, 0, 0)));

    Assert.Equal(unit.State, result.RequireSome());
  }

  [TestCase(TestName = "ExecuteAction delegates to action executor")]
  public void ExecuteActionDelegatesToActionExecutor()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(1, 0, 1)))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    BattleUnitState unit = result.AffectedUnit.RequireSome();
    Assert.True(session.GetUnitPosition(unit).IsSome);
    Assert.Equal(result, runtime.LastActionResult.RequireSome());
  }

  [TestCase(TestName = "Runtime republishes session and action events")]
  public void RuntimeRepublishesSessionAndActionEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var recorder = new BattleEventRecorder(runtime);
    var startedActions = new List<BattleAction>();
    var completedResults = new List<BattleActionResult>();
    runtime.ActionStarted += startedActions.Add;
    runtime.ActionCompleted += completedResults.Add;

    BattleAction action = BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      session.Board.At(1, 0, 1));

    BattleActionResult result = runtime.ExecuteAction(action).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, startedActions.Count);
    Assert.True(object.ReferenceEquals(action, startedActions[0]));
    Assert.Equal(1, completedResults.Count);
    Assert.Equal(result, completedResults[0]);
    Assert.True(recorder.OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "RegisterHook affects runtime action execution")]
  public void RegisterHookAffectsRuntimeActionExecution()
  {
    var solo = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = new BattleRuntime(solo.Session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterHook<UnitMovedBattleEvent>(new RuntimeRecordingHook("runtime_reaction", targetPosition, log), HookPhase.After);

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.MoveUnit(solo.Unit.State, [solo.Session.Board.At(targetPosition)]))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_reaction", log[0]);
  }

  [TestCase(TestName = "RegisterHook event shape affects runtime action execution")]
  public void RegisterHookEventShapeAffectsRuntimeActionExecution()
  {
    var solo = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = new BattleRuntime(solo.Session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterHook<IPositionedBattleEvent>(
      new RuntimeRecordingHook("runtime_shape_reaction", targetPosition, log), HookPhase.After);

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.MoveUnit(solo.Unit.State, [solo.Session.Board.At(targetPosition)]))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
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
    new BattleActionExecutor(session)
      .Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(1, 0, 1)))
      .RequireSingleResult();

    Assert.Equal(0, recorder.All.Count);
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var hook = new RuntimeRecordingHook("runtime_reaction", new Vector3I(1, 0, 0), []);

    runtime.Dispose();

    Assert.Throws<ObjectDisposedException>(() => _ = runtime.LastActionResult);
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetFactionAliveUnits(faction)));
    Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      session.Board.At(1, 0, 1))));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<UnitMovedBattleEvent>(hook, HookPhase.After));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<IPositionedBattleEvent>(
      new RuntimeRecordingHook("runtime_shape_reaction", new Vector3I(1, 0, 0), []), HookPhase.After));
    Assert.Throws<ObjectDisposedException>(() => runtime.UnregisterHook<UnitMovedBattleEvent>(hook, HookPhase.After));
  }

  [TestCase(TestName = "Dispose is idempotent")]
  public void DisposeIsIdempotent()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    runtime.Dispose();
    runtime.Dispose();
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
}
