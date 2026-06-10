using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeTest
{
  [TestCase(TestName = "Query returns query runner result")]
  public void QueryReturnsQueryRunnerResult()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 0));
    var runtime = new BattleRuntime(session);

    BattleBoardState.ValidatedPoint result = GetValue(runtime.Query(new GetUnitPosition(unit.State)));

    Assert.Equal(new Vector3I(1, 0, 0), result.Raw);
  }

  [TestCase(TestName = "ExecuteAction delegates to action executor")]
  public void ExecuteActionDelegatesToActionExecutor()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1)))
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
    var committedEvents = new List<BattleEvent>();
    var startedActions = new List<BattleAction>();
    var completedResults = new List<BattleActionResult>();
    runtime.BattleEventCommitted += committedEvents.Add;
    runtime.ActionStarted += startedActions.Add;
    runtime.ActionCompleted += completedResults.Add;

    BattleAction action = BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      new Vector3I(1, 0, 1));

    BattleActionResult result = runtime.ExecuteAction(action).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, startedActions.Count);
    Assert.True(object.ReferenceEquals(action, startedActions[0]));
    Assert.Equal(1, completedResults.Count);
    Assert.Equal(result, completedResults[0]);
    Assert.True(committedEvents.OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "RegisterTrigger affects runtime action execution")]
  public void RegisterTriggerAffectsRuntimeActionExecution()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);
    var runtime = new BattleRuntime(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterTrigger<UnitMovedBattleEvent>(new RuntimeRecordingTrigger("runtime_trigger", targetPosition, log));

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.MoveUnit(unit.State, [targetPosition]))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_trigger", log[0]);
  }

  [TestCase(TestName = "RegisterTrigger event shape affects runtime action execution")]
  public void RegisterTriggerEventShapeAffectsRuntimeActionExecution()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);
    var runtime = new BattleRuntime(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterTrigger<IPositionedBattleEvent>(
      new RuntimeRecordingTrigger("runtime_shape_trigger", targetPosition, log));

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.MoveUnit(unit.State, [targetPosition]))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_shape_trigger", log[0]);
  }

  [TestCase(TestName = "Disposed runtime no longer republishes session events")]
  public void DisposedRuntimeNoLongerRepublishesSessionEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var committedEvents = new List<BattleEvent>();
    runtime.BattleEventCommitted += committedEvents.Add;

    runtime.Dispose();
    new BattleActionExecutor(session)
      .Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1)))
      .RequireSingleResult();

    Assert.Equal(0, committedEvents.Count);
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    runtime.Dispose();

    Assert.Throws<ObjectDisposedException>(() => _ = runtime.LastActionResult);
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetFactionAliveUnits(faction)));
    Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      new Vector3I(1, 0, 1))));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterTrigger<UnitMovedBattleEvent>(
      new RuntimeRecordingTrigger("runtime_trigger", new Vector3I(1, 0, 0), [])));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterTrigger<IPositionedBattleEvent>(
      new RuntimeRecordingTrigger("runtime_shape_trigger", new Vector3I(1, 0, 0), [])));
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

  private sealed partial class RuntimeRecordingTrigger : BattleTrigger
  {
    private readonly string _message;
    private readonly Vector3I _targetPosition;
    private readonly List<string> _log;

    public RuntimeRecordingTrigger(string message, Vector3I targetPosition, List<string> log)
    {
      _message = message;
      _targetPosition = targetPosition;
      _log = log;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      return battleEvent is UnitMovedBattleEvent movedEvent
        && movedEvent.Position.Raw == _targetPosition;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      _log.Add(_message);
      return BattleTriggerResult.NoReaction();
    }
  }
}
