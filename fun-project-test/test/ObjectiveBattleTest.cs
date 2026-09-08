using System;
using FunProject.Battle;
using FunProject.Weapons;
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
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var data = new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    };
    battle.Session.AddObjective(player, new FakeObjective(data) { Complete = true });
    battle.ClearEvents();
    battle.Start();

    battle.ApplyDamage(battle.UnitAt(new Vector3I(2, 0, 0)), 999);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
    // Stream order: kill, then the flip, then the session end.
    var events = battle.Events.AsValueEnumerable().ToList();
    Assert.True(events.FindIndex(e => e is UnitKilledBattleEvent)
      < events.FindIndex(e => e is ObjectiveCompletedBattleEvent));
    Assert.True(events.FindIndex(e => e is ObjectiveCompletedBattleEvent)
      < events.FindIndex(e => e is SessionEndedBattleEvent));
  }

  [TestCase(TestName = "A SurviveUntilTurn objective wins when its target turn starts")]
  public void SurviveUntilTurnObjectiveWins()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Session.AddObjective(player, new SurviveUntilTurnObjective(new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 2,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }));
    battle.Start();

    battle.AdvanceTurn(); // player turn 1 ends -> enemy turn 1 starts
    battle.AdvanceTurn(); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> Victory mid-submission

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A failed Defeat-directive objective loses the battle the moment its trigger commits")]
  public void DefeatDirectiveEndsBattleOnTheTriggeringEvent()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var data = new FakeObjectiveData
    {
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    };
    battle.Session.AddObjective(player, new FakeObjective(data) { Failed = true });
    battle.Start();

    battle.ApplyDamage(enemyUnit, 999); // ANY observed kill trips the failed objective

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A failed objective can queue a follow-up whose own outcome decides the battle")]
  public void FailedObjectiveQueuesFollowUpWithInstanceBoundOutcome()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var firstEnemy = battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Spawn(TestData.MakeCombatant("E2", enemy), new Vector3I(3, 0, 0));
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
    battle.Session.AddObjective(player, new FakeObjective(main) { Failed = true });
    battle.Start();

    battle.ApplyDamage(firstEnemy, 999); // main fails -> follow-up SurviveUntilTurn(3) queued

    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(2, battle.Session.GetObjectives(player).Count); // main (Failed, history) + follow-up
    Assert.Equal(ObjectiveResult.Failed, battle.Session.GetObjectives(player)[0].State);
    Assert.Equal(ObjectiveResult.Ongoing, battle.Session.GetObjectives(player)[1].State);

    // The follow-up observes TurnStarted and stays Ongoing because the battle only reaches
    // turn 2, proving per-event snapshot semantics and silent non-resolution:
    battle.AdvanceTurn();
    battle.AdvanceTurn();
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
  }

  [TestCase(TestName = "A queued follow-up observes the next event in the same committed batch")]
  public void QueuedFollowUpObservesNextEventInSameBatch()
  {
    var player = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var unit = battle.Spawn(
      TestData.MakeCombatant("Runner", player, actionPoints: 5),
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
    battle.Session.AddObjective(player, main.Instantiate());
    battle.Start();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A queued follow-up can win on its own later event")]
  public void QueuedFollowUpCanWinOnItsOwnLaterEvent()
  {
    var player = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var unit = battle.Spawn(
      TestData.MakeCombatant("Runner", player, actionPoints: 5),
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
    battle.Session.AddObjective(player, main.Instantiate());
    battle.Start();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);

    battle.AdvanceTurn();

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
  }

  [TestCase(TestName = "SurviveUntilTurn target turn one wins during the opening dispatch")]
  public void SurviveUntilTurnTargetTurnOneWinsDuringOpeningDispatch()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Session.AddObjective(player, new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 1,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());

    battle.Start();

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
    Assert.Equal(1, battle.Session.TurnNumber);
  }

  [TestCase]
  public void MixedKillAndStunCompletesEliminationOnlyOnceEvenWhenTheBodyLaterDies()
  {
    using var battle = BattleFixture.Duel();
    var secondEnemy = battle.Spawn(TestData.MakeCombatant("Second", battle.EnemyFaction), new Vector3I(0, 0, 0));
    var objective = battle.Session.GetObjectives(battle.PlayerFaction)[0];
    battle.ClearEvents();

    battle.ApplyDamage(secondEnemy, 20);
    Assert.Equal(ObjectiveResult.Ongoing, objective.State);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction));

    battle.ApplyDamage(battle.EnemyUnit, 20);

    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction));
  }

  [TestCase(typeof(UnitUnconsciousBattleEvent), ObjectiveResult.Ongoing)]
  [TestCase(typeof(BattleEventTag), ObjectiveResult.Passed)]
  public void ObjectivesCannotOverrideFinalPlayerKnockout(Type observedEvent,
    ObjectiveResult expectedObjectiveResult)
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var objective = new FakeObjective(new FakeObjectiveData
    {
      Observe = observedEvent,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    // Adding an objective itself commits an event, which a catch-all observes.
    // Arm completion only after that setup event has drained.
    battle.Session.AddObjective(battle.PlayerFaction, objective);
    objective.Complete = true;
    battle.ClearEvents();

    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetBattleResultQuery()).RequireRight().Outcome);
    Assert.Equal(expectedObjectiveResult, objective.State);
    if (expectedObjectiveResult == ObjectiveResult.Passed)
      battle.Events.EventBefore<SessionEndedBattleEvent, ObjectiveCompletedBattleEvent>();
  }

  [TestCase(TestName = "A silent objective completion records once and leaves the battle running")]
  public void SilentObjectiveCompletionRecordsOnceAndLeavesBattleRunning()
  {
    var player = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var unit = battle.Spawn(
      TestData.MakeCombatant("Runner", player, actionPoints: 5),
      new Vector3I(0, 0, 0));
    battle.Session.AddObjective(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
    }.Instantiate());
    battle.Start();
    battle.ClearEvents();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(0, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A mid-submission objective win drops remaining composite move steps")]
  public void MidSubmissionObjectiveWinDropsRemainingCompositeMoveSteps()
  {
    var player = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player], playerFaction: Some(player));
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = battle.Spawn(
      TestData.MakeCombatant("Runner", player, actionPoints: 5),
      start);
    battle.Session.AddObjective(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    battle.Start();

    battle.Move(unit, [mid, end]);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
    Assert.Equal(mid, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
  }

  [TestCase(TestName = "The player's eliminate-all completes the instant a reaction kills the last enemy during the enemy's turn")]
  public void CrossFactionCompletionResolvesInTheKillingDispatch()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Session.AddObjective(player, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    battle.ClearEvents();
    battle.Start();

    battle.AdvanceTurn(); // now the enemy's turn
    Assert.Equal(enemy, battle.Session.ActiveSide);

    battle.ApplyDamage(enemyUnit, 999); // a reaction kill during the enemy's own turn

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase); // resolved NOW, not at turn end
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == player));
    Assert.Equal(BattleOutcome.Victory, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome);
  }

  [TestCase(TestName = "A wiped player loses instantly even with silent directives")]
  public void WipedPlayerLosesWithSilentDirectives()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Session.AddObjective(player, new FakeObjective(new FakeObjectiveData())); // no directives
    battle.Start();

    battle.ApplyDamage(playerUnit, 999);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome()); // the backstop, not a directive
  }

  [TestCase(DamageKind.Health, TestName = "Directives cannot rescue a wiped player")]
  [TestCase(DamageKind.Stun, TestName = "Directives cannot rescue an unconscious player")]
  public void DirectivesCannotRescueAWipedPlayer(DamageKind kind)
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Session.AddObjective(player, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    battle.ClearEvents();
    battle.Start();

    battle.ApplyDamage(playerUnit, 999, kind); // owner wiped -> backstop Defeat, not the authored rescue directive

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
    Assert.Equal(BattleOutcome.Defeat, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome);
    Assert.Equal(0, battle.Events.EventsOf<ObjectiveFailedBattleEvent>().AsValueEnumerable().Count());
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
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    battle.Session.AddObjective(player, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    // Priority 0 fires BEFORE ObjectiveSystem (priority 100): the interrupt is queued,
    // then the objective ends the battle — the guard must drop the queued interrupt.
    battle.RegisterHook<UnitKilledBattleEvent>(new QueueInterruptOnKill(
      BattleAction.PassUnit(battle.Session.TryGetAlive(playerUnit).RequireSome())), 0);
    battle.Start();

    battle.ApplyDamage(enemyUnit, 999); // without the guard this throws (PassUnit requires InProgress)

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
  }
}
