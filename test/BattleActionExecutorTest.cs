using FunProject.Battle;
using Godot;
using System;

public partial class BattleActionExecutorTest : TestRunner
{
  public override void _Ready()
  {
    T("Executor resolves a legal move step", () =>
    {
      var session = new BattleSession(4, 4, 2);
      var faction = BattleTestFactory.MakeFaction("Player");
      var unit = session.AddUnit(BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      executor.Enqueue(BattleActionIntent.MoveStep(unit.UnitId, new Vector3I(1, 1, 1), 2));

      var result = executor.Tick();

      Assert.True(result.HasValue);
      Assert.True(result!.Value.Succeeded);
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
      Assert.Equal(3, unit.CurrentActionPoints);
    });

    T("Executor resolves queued actions in FIFO order", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var factionA = BattleTestFactory.MakeFaction("A");
      var factionB = BattleTestFactory.MakeFaction("B");
      var unitA = session.AddUnit(BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 4), new Vector3I(0, 0, 0));
      session.AddUnit(BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      executor.Enqueue(BattleActionIntent.MoveStep(unitA.UnitId, new Vector3I(0, 0, 1)));
      executor.Enqueue(BattleActionIntent.EndFactionTurn(factionA));

      var first = executor.Tick();
      var second = executor.Tick();

      Assert.True(first.HasValue);
      Assert.True(first!.Value.Succeeded);
      Assert.True(first.Value.Intent is MoveStepBattleActionIntent);
      Assert.True(second.HasValue);
      Assert.True(second!.Value.Succeeded);
      Assert.True(second.Value.Intent is EndFactionTurnBattleActionIntent);
      Assert.Equal(factionB, session.ActiveSide);
    });

    T("Executor resolves pass unit by selecting the next available ally", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var faction = BattleTestFactory.MakeFaction("Player");
      var unitA = session.AddUnit(BattleTestFactory.MakeCombatant("A1", faction), new Vector3I(0, 0, 0));
      var unitB = session.AddUnit(BattleTestFactory.MakeCombatant("A2", faction), new Vector3I(1, 0, 0));
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      executor.Enqueue(BattleActionIntent.PassUnit(unitA.UnitId));

      var result = executor.Tick();

      Assert.True(result.HasValue);
      Assert.True(result!.Value.Succeeded);
      Assert.True(unitA.HasEndedActivationThisTurn);
      Assert.Equal(unitB.UnitId, session.SelectedUnitId!.Value);
      Assert.Equal(faction, session.ActiveSide);
    });

    T("Executor reports unsupported actions cleanly", () =>
    {
      var session = new BattleSession(3, 3, 1);
      var executor = new BattleActionExecutor(session);
      executor.Enqueue(BattleActionIntent.Named("unknown_action", 1));

      var result = executor.Tick();

      Assert.True(result.HasValue);
      Assert.False(result!.Value.Succeeded);
      Assert.Equal(BattleActionFailureReason.UnsupportedAction, result.Value.FailureReason);
    });

    T("Executor emits action resolved events", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var faction = BattleTestFactory.MakeFaction("Player");
      var unit = session.AddUnit(BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      BattleActionExecutionResult? resolvedResult = null;
      executor.ActionResolved += result => resolvedResult = result;

      executor.Enqueue(BattleActionIntent.MoveStep(unit.UnitId, new Vector3I(1, 0, 2)));
      executor.Tick();

      Assert.True(resolvedResult.HasValue);
      Assert.True(resolvedResult!.Value.Succeeded);
      Assert.True(resolvedResult.Value.Intent is MoveStepBattleActionIntent);
    });

    T("Executor drain queue returns all action results", () =>
    {
      var session = new BattleSession(5, 5, 1);
      var factionA = BattleTestFactory.MakeFaction("A");
      var factionB = BattleTestFactory.MakeFaction("B");
      var unitA = session.AddUnit(BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 5), new Vector3I(0, 0, 0));
      var unitB = session.AddUnit(BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(3, 0, 0));
      var grenade = BattleTestFactory.MakeGrenade("Practice");
      unitA.AddInventoryItem(grenade);
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      executor.Enqueue(BattleActionIntent.MoveStep(unitA.UnitId, new Vector3I(0, 0, 1)));
      executor.Enqueue(BattleActionIntent.ThrowItem(unitA.UnitId, grenade, new Vector3I(2, 0, 1)));
      executor.Enqueue(BattleActionIntent.EndFactionTurn(factionA));

      var results = executor.DrainQueue();

      Assert.Equal(3, results.Count);
      Assert.True(results[0].Succeeded);
      Assert.True(results[1].Succeeded);
      Assert.True(results[2].Succeeded);
      Assert.Equal(factionB, session.ActiveSide);
      Assert.False(unitA.HasInventoryItem(grenade));
      Assert.Equal(unitB.UnitId, session.SelectedUnitId!.Value);
    });

    T("Executor rejects stale end faction turn intent after auto-advance", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var factionA = BattleTestFactory.MakeFaction("A");
      var factionB = BattleTestFactory.MakeFaction("B");
      var unitA = session.AddUnit(BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      session.AddUnit(BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      executor.Enqueue(BattleActionIntent.PassUnit(unitA.UnitId));
      executor.Enqueue(BattleActionIntent.EndFactionTurn(factionA));

      var first = executor.Tick();
      var second = executor.Tick();

      Assert.True(first.HasValue);
      Assert.True(first!.Value.Succeeded);
      Assert.Equal(factionB, session.ActiveSide);
      Assert.True(second.HasValue);
      Assert.False(second!.Value.Succeeded);
      Assert.Equal(BattleActionFailureReason.ActionRejected, second.Value.FailureReason);
      Assert.Equal(factionB, session.ActiveSide);
    });

    T("Executor recovers when ActionStarted handler throws", () =>
    {
      var session = new BattleSession(4, 4, 2);
      var faction = BattleTestFactory.MakeFaction("Player");
      var unit = session.AddUnit(BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
      session.StartBattle();

      var executor = new BattleActionExecutor(session);
      bool shouldThrow = true;
      executor.ActionStarted += _ =>
      {
        if (!shouldThrow)
          return;

        shouldThrow = false;
        throw new InvalidOperationException("boom");
      };

      executor.Enqueue(BattleActionIntent.MoveStep(unit.UnitId, new Vector3I(1, 0, 2), 2));
      executor.Enqueue(BattleActionIntent.MoveStep(unit.UnitId, new Vector3I(1, 1, 1), 2));

      var first = executor.Tick();
      var second = executor.Tick();

      Assert.True(first.HasValue);
      Assert.False(first!.Value.Succeeded);
      Assert.Equal(BattleActionFailureReason.UnexpectedError, first.Value.FailureReason);
      Assert.False(executor.IsBusy);
      Assert.True(executor.ActiveIntent == null);
      Assert.Equal(new Vector3I(1, 0, 1), unit.Position);

      Assert.True(second.HasValue);
      Assert.True(second!.Value.Succeeded);
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
    });

    Report();
  }
}
