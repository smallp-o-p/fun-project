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
  [TestCase(true, TestName = "A completed Victory-directive objective ends the battle the moment its trigger commits")]
  [TestCase(false, TestName = "A failed Defeat-directive objective loses the battle the moment its trigger commits")]
  public void DirectiveEndsBattleOnTheTriggeringEvent(bool complete)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true, start: false,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)));
    battle.AddObjective(battle.PlayerFaction, new FakeObjective(new FakeObjectiveData
    {
      OnComplete = complete ? new EndBattleDirectiveData { Outcome = BattleOutcome.Victory } : null,
      OnFail = complete ? null : new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    })
    {
      Complete = complete,
      Failed = !complete,
    });
    if (complete)
      battle.ClearEvents(); // the victory row pins the stream order from a clean window
    battle.Start();

    battle.ApplyDamage(battle.EnemyUnit, 999); // ANY observed kill trips either directive

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(complete ? BattleOutcome.Victory : BattleOutcome.Defeat,
      battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    if (!complete)
      return;

    // Stream order: kill, then the flip, then the session end.
    var events = battle.Events.AsValueEnumerable().ToList();
    Assert.True(events.FindIndex(e => e is UnitKilledBattleEvent)
      < events.FindIndex(e => e is ObjectiveCompletedBattleEvent));
    Assert.True(events.FindIndex(e => e is ObjectiveCompletedBattleEvent)
      < events.FindIndex(e => e is SessionEndedBattleEvent));
  }

  [TestCase(2, TestName = "A SurviveUntilTurn objective wins when its target turn starts")]
  [TestCase(1, TestName = "SurviveUntilTurn target turn one wins during the opening dispatch")]
  public void SurviveUntilTurnObjectiveWinsAtTargetTurn(int targetTurn)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true, start: false,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)));
    battle.AddObjective(battle.PlayerFaction, new SurviveUntilTurnObjectiveData
    {
      TargetTurn = targetTurn,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    battle.Start();

    if (targetTurn == 2)
    {
      battle.AdvanceTurn(); // player turn 1 ends -> enemy turn 1 starts
      battle.AdvanceTurn(); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> Victory mid-submission
    }

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(targetTurn, battle.Query(new GetCompletedBattleQuery()).RequireSome().TurnCount);
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
    battle.AddObjective(player, new FakeObjective(main) { Failed = true });
    battle.Start();

    battle.ApplyDamage(firstEnemy, 999); // main fails -> follow-up SurviveUntilTurn(3) queued

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    var objectives = battle.Query(new GetObjectivesForFaction(player)).RequireSome();
    Assert.Equal(2, objectives.Count); // main (Failed, history) + follow-up
    Assert.Equal(ObjectiveResult.Failed, objectives[0].State);
    Assert.Equal(ObjectiveResult.Ongoing, objectives[1].State);

    // The follow-up observes TurnStarted and stays Ongoing because the battle only reaches
    // turn 2, proving per-event snapshot semantics and silent non-resolution:
    battle.AdvanceTurn();
    battle.AdvanceTurn();
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
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
    battle.AddObjective(player, main.Instantiate());
    battle.Start();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
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
    battle.AddObjective(player, main.Instantiate());
    battle.Start();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);

    battle.AdvanceTurn();

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
  }

  [TestCase]
  public void MixedKillAndStunCompletesEliminationOnlyOnceEvenWhenTheBodyLaterDies()
  {
    using var battle = BattleFixture.Duel();
    var secondEnemy = battle.Spawn(TestData.MakeCombatant("Second", battle.EnemyFaction), new Vector3I(0, 0, 0));
    var objective = battle.Query(new GetObjectivesForFaction(battle.PlayerFaction)).RequireSome()[0];
    battle.ClearEvents();

    battle.ApplyDamage(secondEnemy, 20);
    Assert.Equal(ObjectiveResult.Ongoing, objective.State);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction));

    // Killing the unconscious body later still cannot re-resolve the flipped objective.
    battle.ApplyDamage(battle.EnemyUnit, 20);

    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction));
  }

  [TestCase(typeof(UnitUnconsciousBattleEvent), ObjectiveResult.Ongoing)]
  [TestCase(typeof(BattleEventTag), ObjectiveResult.Passed)]
  public void ObjectivesCannotOverrideFinalPlayerKnockout(Type observedEvent,
    ObjectiveResult expectedObjectiveResult)
  {
    using var battle = BattleFixture.Duel(playerControlled: true, start: false);
    var objective = new FakeObjective(new FakeObjectiveData
    {
      Observe = observedEvent,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    battle.AddObjective(battle.PlayerFaction, objective);
    battle.Start();
    // Arm completion only after the opening events have drained: the next observed event
    // is the knockout itself.
    objective.Complete = true;
    battle.ClearEvents();

    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
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
    battle.AddObjective(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
    }.Instantiate());
    battle.Start();
    battle.ClearEvents();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(0, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A mid-submission objective win drops remaining composite move steps and their bookkeeping")]
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
    battle.AddObjective(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    battle.Start();

    battle.Move(unit, [mid, end]);

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    // Exactly one tile step committed with its full bookkeeping; the second tile never ran.
    Assert.Equal(mid, battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(1, battle.Events.EventsOf<TileOccupiedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(4, unit.CurrentActionPoints);
    // Exactly one end event, and it is the last terminal notification in the stream.
    Assert.Equal(1, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(typeof(SessionEndedBattleEvent),
      battle.Events.AsValueEnumerable().Last().GetType());
  }

  [TestCase(TestName = "The player's eliminate-all completes the instant a reaction kills the last enemy during the enemy's turn")]
  public void CrossFactionCompletionResolvesInTheKillingDispatch()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true, start: false,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)));
    battle.AddObjective(battle.PlayerFaction, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    battle.ClearEvents();
    battle.Start();

    battle.AdvanceTurn(); // now the enemy's turn
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.ApplyDamage(battle.EnemyUnit, 999); // a reaction kill during the enemy's own turn

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome); // resolved NOW, not at turn end
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction));
    Assert.Equal(BattleOutcome.Victory, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome);
  }

  [TestCase(TestName = "A wiped player loses instantly even with silent directives")]
  public void WipedPlayerLosesWithSilentDirectives()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true, start: false,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)));
    battle.AddObjective(battle.PlayerFaction, new FakeObjective(new FakeObjectiveData())); // no directives
    battle.Start();

    battle.ApplyDamage(battle.PlayerUnit, 999);

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome); // the backstop, not a directive
  }

  [TestCase(DamageKind.Health, TestName = "Directives cannot rescue a wiped player")]
  [TestCase(DamageKind.Stun, TestName = "Directives cannot rescue an unconscious player")]
  public void DirectivesCannotRescueAWipedPlayer(DamageKind kind)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true, start: false,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)));
    battle.AddObjective(battle.PlayerFaction, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    battle.ClearEvents();
    battle.Start();

    battle.ApplyDamage(battle.PlayerUnit, 999, kind); // owner wiped -> backstop Defeat, not the authored rescue directive

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(BattleOutcome.Defeat, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome);
    Assert.Equal(0, battle.Events.EventsOf<ObjectiveFailedBattleEvent>().AsValueEnumerable().Count());
  }

  private sealed class QueueInterruptOnKill(Func<HookContext, BattleAction> build) : BattleHook
  {
    private bool _queued;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (_queued || battleEvent is not UnitKilledBattleEvent)
        return [];
      _queued = true;
      return [build(context)];
    }
  }

  [TestCase(TestName = "A mid-submission end drops queued interrupts and they never execute")]
  public void MidSubmissionEndDropsQueuedInterrupts()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true, start: false,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)));
    battle.AddObjective(battle.PlayerFaction, new EliminateAllOpposingForcesObjective(
      new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    // Priority 0 fires BEFORE ObjectiveSystem (priority 100): the interrupt is queued,
    // then the objective ends the battle — the guard must drop the queued interrupt.
    battle.RegisterHook<UnitKilledBattleEvent>(new QueueInterruptOnKill(
      context => BattleAction.PassUnit(
        context.Read.State.TryGetAlive(battle.PlayerUnit).RequireSome())), 0);
    battle.Start();
    battle.ClearEvents();

    battle.ApplyDamage(battle.EnemyUnit, 999); // without the guard this throws (PassUnit against a completed battle)

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    // The discarded interrupt ran nothing: no activation-ended event after the settlement.
    Assert.Equal(0, battle.Events.EventsOf<UnitActivationEndedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(1, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(typeof(SessionEndedBattleEvent),
      battle.Events.AsValueEnumerable().Last().GetType());
  }
}
