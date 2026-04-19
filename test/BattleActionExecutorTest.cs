using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public class BattleActionExecutorTest
{
  [TestCase(TestName = "Executor resolves a legal move step mutation")]
  public void ExecutorResolvesALegalMoveStepMutation()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 2, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 1, 1), 2));

    var result = executor.Tick();

    Assert.True(result.HasValue);
    Assert.True(result!.Value.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Executor evaluates a legal move step mutation without mutating state")]
  public void ExecutorEvaluatesALegalMoveStepMutationWithoutMutatingState()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);

    var evaluation = executor.Evaluate(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 0, 2), 2));

    Assert.True(evaluation.IsAllowed);
    Assert.True(evaluation.Mutation is MoveUnitStep);
    Assert.Equal(2, evaluation.ActionPointCost);
    Assert.Equal(new Vector3I(1, 0, 1), unit.Position);
    Assert.Equal(5, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Executor evaluates an invalid move step mutation without mutating state")]
  public void ExecutorEvaluatesAnInvalidMoveStepMutationWithoutMutatingState()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);

    var evaluation = executor.Evaluate(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(2, 0, 0)));

    Assert.False(evaluation.IsAllowed);
    Assert.Equal(BattleMutationFailureReason.Rejected, evaluation.FailureReason);
    Assert.True(evaluation.Mutation is MoveUnitStep);
    Assert.Equal(new Vector3I(0, 0, 0), unit.Position);
    Assert.Equal(4, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Executor evaluates move mutation action point cost")]
  public void ExecutorEvaluatesMoveMutationActionPointCost()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    Vector3I[] path = session.Board.FindPath(unit.Position, new Vector3I(2, 0, 0), unit.UnitId);
    Assert.Equal(3, path.Length);

    var evaluation = executor.Evaluate(BattleSessionMutation.MoveUnit(unit.UnitId, path));

    Assert.True(evaluation.IsAllowed);
    Assert.Equal(2, evaluation.ActionPointCost);
    Assert.Equal(new Vector3I(0, 0, 0), unit.Position);
    Assert.Equal(5, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Executor resolves queued mutations in FIFO order")]
  public void ExecutorResolvesQueuedMutationsInFifoOrder()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 4), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unitA.UnitId, new Vector3I(0, 0, 1)));
    executor.Enqueue(BattleSessionMutation.EndFactionTurn(factionA));

    var first = executor.Tick();
    var second = executor.Tick();

    Assert.True(first.HasValue);
    Assert.True(first!.Value.Succeeded);
    Assert.True(first.Value.Mutation is MoveUnitStep);
    Assert.True(second.HasValue);
    Assert.True(second!.Value.Succeeded);
    Assert.True(second.Value.Mutation is EndFactionTurn);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Executor resolves pass unit while keeping the next ally available")]
  public void ExecutorResolvesPassUnitWhileKeepingTheNextAllyAvailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", faction), new Vector3I(0, 0, 0));
    var unitB = SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.Enqueue(BattleSessionMutation.PassUnit(unitA.UnitId));

    var result = executor.Tick();

    Assert.True(result.HasValue);
    Assert.True(result!.Value.Succeeded);
    Assert.False(session.IsUnitStillAvailableThisTurn(unitA.UnitId));
    Assert.False(session.CanUnitActNow(unitA.UnitId));
    Assert.True(session.IsUnitStillAvailableThisTurn(unitB.UnitId));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase(TestName = "Executor returns rejected mutation results cleanly")]
  public void ExecutorReturnsRejectedMutationResultsCleanly()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(2, 0, 0)));

    var result = executor.Tick();

    Assert.True(result.HasValue);
    Assert.False(result!.Value.Succeeded);
    Assert.Equal(BattleMutationFailureReason.Rejected, result.Value.FailureReason);
  }

  [TestCase(TestName = "Executor emits mutation resolved events")]
  public void ExecutorEmitsMutationResolvedEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleMutationResult? resolvedResult = null;
    executor.MutationResolved += result => resolvedResult = result;

    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 0, 2)));
    executor.Tick();

    Assert.True(resolvedResult.HasValue);
    Assert.True(resolvedResult!.Value.Succeeded);
    Assert.True(resolvedResult.Value.Mutation is MoveUnitStep);
  }

  [TestCase(TestName = "Executor drain queue returns all mutation results")]
  public void ExecutorDrainQueueReturnsAllMutationResults()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 5), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(3, 0, 0));
    var grenade = BattleTestFactory.MakeGrenade("Practice");
    unitA.AddInventoryItem(grenade);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unitA.UnitId, new Vector3I(0, 0, 1)));
    executor.Enqueue(BattleSessionMutation.ThrowItem(unitA.UnitId, grenade, new Vector3I(2, 0, 1)));
    executor.Enqueue(BattleSessionMutation.EndFactionTurn(factionA));

    var results = executor.DrainQueue();

    Assert.Equal(3, results.Count);
    Assert.True(results[0].Succeeded);
    Assert.True(results[1].Succeeded);
    Assert.True(results[2].Succeeded);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.False(unitA.HasInventoryItem(grenade));
  }

  [TestCase(TestName = "Executor rejects stale end faction turn mutation after auto-advance")]
  public void ExecutorRejectsStaleEndFactionTurnMutationAfterAutoAdvance()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.Enqueue(BattleSessionMutation.PassUnit(unitA.UnitId));
    executor.Enqueue(BattleSessionMutation.EndFactionTurn(factionA));

    var first = executor.Tick();
    var second = executor.Tick();

    Assert.True(first.HasValue);
    Assert.True(first!.Value.Succeeded);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.True(second.HasValue);
    Assert.False(second!.Value.Succeeded);
    Assert.Equal(BattleMutationFailureReason.Rejected, second.Value.FailureReason);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Executor recovers when MutationStarted handler throws")]
  public void ExecutorRecoversWhenMutationStartedHandlerThrows()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 2, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    bool shouldThrow = true;
    executor.MutationStarted += _ =>
    {
      if (!shouldThrow)
        return;

      shouldThrow = false;
      throw new InvalidOperationException("boom");
    };

    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 0, 2), 2));
    executor.Enqueue(BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 1, 1), 2));

    var first = executor.Tick();
    var second = executor.Tick();

    Assert.True(first.HasValue);
    Assert.False(first!.Value.Succeeded);
    Assert.Equal(BattleMutationFailureReason.UnexpectedError, first.Value.FailureReason);
    Assert.False(executor.IsBusy);
    Assert.True(executor.ActiveMutation == null);

    Assert.True(second.HasValue);
    Assert.True(second!.Value.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
  }

  [TestCase(TestName = "Executor returns null when the queue is empty")]
  public void ExecutorReturnsNullWhenTheQueueIsEmpty()
  {
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2));
    var executor = new BattleActionExecutor(session);

    var result = executor.Tick();

    Assert.False(result.HasValue);
    Assert.True(executor.LastResult == null);
  }

  private static BattleUnitState SpawnUnit(BattleSession session, Combatant combatant, Vector3I position)
  {
    var result = BattleSessionMutation.SpawnUnit(combatant, position).Execute(session);
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit != null);
    return result.AffectedUnit!;
  }

  private static void StartBattle(BattleSession session)
  {
    var result = BattleSessionMutation.StartBattle().Execute(session);
    Assert.True(result.Succeeded);
  }
}
