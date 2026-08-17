using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveBattleTest
{
  [TestCase(TestName = "A completed Victory-directive objective ends the battle the moment its trigger commits")]
  public void VictoryDirectiveEndsBattleOnTheTriggeringEvent()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var data = new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    };
    session.AddObjective(player, new FakeObjective(data) { Complete = true });
    var recorder = new BattleEventRecorder(session);
    StartBattle(session);

    ApplyDamage(session, session.GetUnitAt(session.Board.At(new Vector3I(2, 0, 0))).RequireSome(), 999);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    // Stream order: kill, then the flip, then the session end.
    var events = recorder.All.AsValueEnumerable().ToList();
    Assert.True(events.FindIndex(e => e is UnitKilledBattleEvent)
      < events.FindIndex(e => e is ObjectiveCompletedBattleEvent));
    Assert.True(events.FindIndex(e => e is ObjectiveCompletedBattleEvent)
      < events.FindIndex(e => e is SessionEndedBattleEvent));
  }

  [TestCase(TestName = "A SurviveUntilTurn objective wins when its target turn starts")]
  public void SurviveUntilTurnObjectiveWins()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new SurviveUntilTurnObjective(new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 2,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }));
    StartBattle(session);

    AdvanceTurn(session); // player turn 1 ends -> enemy turn 1 starts
    AdvanceTurn(session); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> Victory mid-submission

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A failed Defeat-directive objective loses the battle the moment its trigger commits")]
  public void DefeatDirectiveEndsBattleOnTheTriggeringEvent()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var data = new FakeObjectiveData
    {
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    };
    session.AddObjective(player, new FakeObjective(data) { Failed = true });
    StartBattle(session);

    ApplyDamage(session, enemyUnit, 999); // ANY observed kill trips the failed objective

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A failed objective can queue a follow-up whose own outcome decides the battle")]
  public void FailedObjectiveQueuesFollowUpWithInstanceBoundOutcome()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var firstEnemy = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E2", enemy), new Vector3I(3, 0, 0));
    var main = new FakeObjectiveData
    {
      OnFail = new QueueDirectiveData
      {
        FollowUps =
        [
          new SurviveUntilTurnObjectiveData
          {
            TargetTurn = 3, // later than this battle will reach — stays Ongoing
            OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
          },
        ],
      },
    };
    session.AddObjective(player, new FakeObjective(main) { Failed = true });
    StartBattle(session);

    ApplyDamage(session, firstEnemy, 999); // main fails -> follow-up SurviveUntilTurn(3) queued

    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.Equal(2, session.GetObjectives(player).Count); // main (Failed, history) + follow-up
    Assert.Equal(ObjectiveResult.Failed, session.GetObjectives(player)[0].State);
    Assert.Equal(ObjectiveResult.Ongoing, session.GetObjectives(player)[1].State);

    // The follow-up observes TurnStarted and stays Ongoing because the battle only reaches
    // turn 2, proving per-event snapshot semantics and silent non-resolution:
    AdvanceTurn(session);
    AdvanceTurn(session);
    Assert.Equal(BattlePhase.InProgress, session.Phase);
  }

  [TestCase(TestName = "A queued follow-up observes the next event in the same committed batch")]
  public void QueuedFollowUpObservesNextEventInSameBatch()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var unit = SpawnUnit(
      session,
      BattleTestFactory.MakeCombatant("Runner", player, actionPoints: 5),
      new Vector3I(0, 0, 0));
    var followUp = new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(TileOccupiedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    };
    var main = new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
      OnComplete = new QueueDirectiveData { FollowUps = [followUp] },
    };
    session.AddObjective(player, main.Instantiate());
    StartBattle(session);

    ExecutorFor(session).Submit(BattleAction.MoveUnit(
      unit.AliveIn(session),
      [session.Board.At(1, 0, 0)]));

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A queued follow-up can win on its own later event")]
  public void QueuedFollowUpCanWinOnItsOwnLaterEvent()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var unit = SpawnUnit(
      session,
      BattleTestFactory.MakeCombatant("Runner", player, actionPoints: 5),
      new Vector3I(0, 0, 0));
    var followUp = new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(TurnStartedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    };
    var main = new FakeObjectiveData
    {
      Failed = true,
      Observe = typeof(UnitMovedBattleEvent),
      OnFail = new QueueDirectiveData { FollowUps = [followUp] },
    };
    session.AddObjective(player, main.Instantiate());
    StartBattle(session);

    ExecutorFor(session).Submit(BattleAction.MoveUnit(
      unit.AliveIn(session),
      [session.Board.At(1, 0, 0)]));
    Assert.Equal(BattlePhase.InProgress, session.Phase);

    AdvanceTurn(session);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "SurviveUntilTurn target turn one wins during the opening dispatch")]
  public void SurviveUntilTurnTargetTurnOneWinsDuringOpeningDispatch()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 1,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());

    StartBattle(session);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    Assert.Equal(1, session.TurnNumber);
  }

  [TestCase(TestName = "A silent objective completion records once and leaves the battle running")]
  public void SilentObjectiveCompletionRecordsOnceAndLeavesBattleRunning()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var unit = SpawnUnit(
      session,
      BattleTestFactory.MakeCombatant("Runner", player, actionPoints: 5),
      new Vector3I(0, 0, 0));
    session.AddObjective(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
    }.Instantiate());
    StartBattle(session);
    var recorder = new BattleEventRecorder(session);

    ExecutorFor(session).Submit(BattleAction.MoveUnit(
      unit.AliveIn(session),
      [session.Board.At(1, 0, 0)]));

    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.Equal(1, recorder.OfType<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(0, recorder.OfType<SessionEndedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A mid-submission objective win drops remaining composite move steps")]
  public void MidSubmissionObjectiveWinDropsRemainingCompositeMoveSteps()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(
      session,
      BattleTestFactory.MakeCombatant("Runner", player, actionPoints: 5),
      start);
    session.AddObjective(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    StartBattle(session);

    ExecutorFor(session).Submit(BattleAction.MoveUnit(
      unit.AliveIn(session),
      [session.Board.At(mid), session.Board.At(end)]));

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    Assert.Equal(mid, session.GetUnitPosition(unit.State).RequireSome().Raw);
  }

  [TestCase(TestName = "The player's eliminate-all completes the instant a reaction kills the last enemy during the enemy's turn")]
  public void CrossFactionCompletionResolvesInTheKillingDispatch()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    var recorder = new BattleEventRecorder(session);
    StartBattle(session);

    AdvanceTurn(session); // now the enemy's turn
    Assert.Equal(enemy, session.ActiveSide);

    ApplyDamage(session, enemyUnit, 999); // a reaction kill during the enemy's own turn

    Assert.Equal(BattlePhase.Ended, session.Phase); // resolved NOW, not at turn end
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    Assert.Equal(1, recorder.OfType<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == player));
    Assert.Equal(BattleOutcome.Victory, recorder.OfType<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome);
  }

  [TestCase(TestName = "A wiped player loses instantly even with silent directives")]
  public void WipedPlayerLosesWithSilentDirectives()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new FakeObjective(new FakeObjectiveData())); // no directives
    StartBattle(session);

    ApplyDamage(session, playerUnit, 999);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome()); // the backstop, not a directive
  }

  [TestCase(TestName = "Directives cannot rescue a wiped player")]
  public void DirectivesCannotRescueAWipedPlayer()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    var recorder = new BattleEventRecorder(session);
    StartBattle(session);

    ApplyDamage(session, playerUnit, 999); // owner wiped -> backstop Defeat, not the authored rescue directive

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
    Assert.Equal(BattleOutcome.Defeat, recorder.OfType<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome);
    Assert.Equal(0, recorder.OfType<ObjectiveFailedBattleEvent>().AsValueEnumerable().Count());
  }

  private sealed class QueueInterruptOnKill(BattleAction interrupt) : BattleHook
  {
    private bool _queued;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (_queued || battleEvent is not UnitKilledBattleEvent)
        return [];
      _queued = true;
      return [interrupt];
    }
  }

  [TestCase(TestName = "A queued interrupt never runs after the battle ends mid-submission")]
  public void MidSubmissionEndDropsQueuedInterrupts()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    // Priority 0 fires BEFORE ObjectiveSystem (priority 100): the interrupt is queued,
    // then the objective ends the battle — the guard must drop the queued interrupt.
    ExecutorFor(session).RegisterHook<UnitKilledBattleEvent>(new QueueInterruptOnKill(
      BattleAction.PassUnit(session.TryGetAlive(playerUnit).RequireSome())), 0);
    StartBattle(session);

    ApplyDamage(session, enemyUnit, 999); // without the guard this throws (PassUnit requires InProgress)

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }
}
