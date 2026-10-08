using System;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveBattleTest
{
  // Replaces the default player-side objectives with authored ones through the fixture's
  // setup transformation, preserving the old pre-Start AddObjective semantics.
  private static Func<BattleSetup, BattleSetup> PlayerObjectives(params ObjectiveData[] objectives) =>
    setup => setup with { Sides = [setup.Sides[0] with { Objectives = objectives }, setup.Sides[1]] };

  private static BattleFixture RunnerFixture(Faction player, params ObjectiveData[] objectives)
    => new(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [new BattleSideSetup(player, objectives,
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Runner", player, actionPoints: 5)), new Vector3I(0, 0, 0))])],
      Seed: 7,
      PlayerFaction: Some(player)));

  [TestCase(true, TestName = "A completed Victory-directive objective ends the battle the moment its trigger commits")]
  [TestCase(false, TestName = "A failed Defeat-directive objective loses the battle the moment its trigger commits")]
  public void DirectiveEndsBattleOnTheTriggeringEvent(bool complete)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)),
      configure: PlayerObjectives(new FakeObjectiveData
      {
        Complete = complete,
        Failed = !complete,
        OnComplete = complete ? new EndBattleDirectiveData { Outcome = BattleOutcome.Victory } : null,
        OnFail = complete ? null : new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
      }));
    battle.ClearEvents(); // both rows pin the stream order from a clean window

    battle.ApplyDamage(battle.EnemyUnit, 999); // ANY observed kill trips either directive

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(complete ? BattleOutcome.Victory : BattleOutcome.Defeat,
      battle.Session.Outcome.RequireSome()));
    if (!complete)
      return; // the defeat row flips through ObjectiveFailedBattleEvent, not the completed event below

    // Stream order: kill, then the flip, then the session end.
    var events = battle.Events.AsValueEnumerable().ToList();
    battle.Read(() => Assert.True(events.FindIndex(e => e is UnitKilledBattleEvent)
      < events.FindIndex(e => e is ObjectiveCompletedBattleEvent)));
    battle.Read(() => Assert.True(events.FindIndex(e => e is ObjectiveCompletedBattleEvent)
      < events.FindIndex(e => e is SessionEndedBattleEvent)));
  }

  [TestCase(2, TestName = "A SurviveUntilTurn objective wins when its target turn starts")]
  [TestCase(1, TestName = "SurviveUntilTurn target turn one wins during the opening dispatch")]
  public void SurviveUntilTurnObjectiveWinsAtTargetTurn(int targetTurn)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)),
      configure: PlayerObjectives(new SurviveUntilTurnObjectiveData
      {
        TargetTurn = targetTurn,
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));

    if (targetTurn == 2)
    {
      battle.AdvanceTurn(); // player turn 1 ends -> enemy turn 1 starts
      battle.AdvanceTurn(); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> Victory mid-submission
    }

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
    battle.Read(() => Assert.Equal(targetTurn, battle.Session.TurnNumber));
  }

  [TestCase(TestName = "An activation-ended objective win stops the turn continuation")]
  public void ActivationCompletionStopsTurnContinuation()
  {
    using var battle = BattleFixture.Duel();
    battle.Mutate(() => battle.Session.AddObjective(battle.PlayerFaction, new FakeObjective(new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitActivationEndedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    })));
    battle.ClearEvents();

    battle.Pass(battle.PlayerUnit);

    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<SessionEndedBattleEvent>().Length));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<TurnEndedBattleEvent>().Length));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<TurnStartedBattleEvent>().Length));
  }

  [TestCase(TestName = "A failed objective can queue a follow-up whose own outcome decides the battle")]
  public void FailedObjectiveQueuesFollowUpWithInstanceBoundOutcome()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var main = new FakeObjectiveData
    {
      Failed = true,
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
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(5, 1, 5)),
      [
        new BattleSideSetup(player, [main],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("P1", player)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemy, [new FakeObjectiveData()],
          [
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("E1", enemy)), new Vector3I(2, 0, 0)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("E2", enemy)), new Vector3I(3, 0, 0)),
          ]),
      ],
      Seed: 7,
      PlayerFaction: Some(player)));
    var firstEnemy = battle.UnitAt(new Vector3I(2, 0, 0));

    battle.ApplyDamage(firstEnemy, 999); // main fails -> follow-up SurviveUntilTurn(3) queued

    battle.Read(() => Assert.True(battle.Session.Outcome.IsNone));
    battle.Read(() => Assert.Equal(2, battle.Session.GetObjectives(player).Count)); // main (Failed, history) + follow-up
    battle.Read(() => Assert.Equal(ObjectiveResult.Failed, battle.Session.GetObjectives(player)[0].State));
    battle.Read(() => Assert.Equal(ObjectiveResult.Ongoing, battle.Session.GetObjectives(player)[1].State));

    // The follow-up observes TurnStarted and stays Ongoing because the battle only reaches
    // turn 2, proving per-event snapshot semantics and silent non-resolution:
    battle.AdvanceTurn();
    battle.AdvanceTurn();
    battle.Read(() => Assert.True(battle.Session.Outcome.IsNone));
  }

  [TestCase(TestName = "A queued follow-up observes the next event in the same committed batch")]
  public void QueuedFollowUpObservesNextEventInSameBatch()
  {
    var player = TestData.MakeFaction("Player");
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
    using var battle = RunnerFixture(player, main);
    var unit = battle.Unit;

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
  }

  [TestCase(TestName = "A queued follow-up can win on its own later event")]
  public void QueuedFollowUpCanWinOnItsOwnLaterEvent()
  {
    var player = TestData.MakeFaction("Player");
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
    using var battle = RunnerFixture(player, main);
    var unit = battle.Unit;

    battle.Move(unit, [new Vector3I(1, 0, 0)]);
    battle.Read(() => Assert.True(battle.Session.Outcome.IsNone));

    battle.AdvanceTurn();

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
  }

  [TestCase]
  public void MixedKillAndStunCompletesEliminationOnlyOnceEvenWhenTheBodyLaterDies()
  {
    using var battle = BattleFixture.Duel();
    var secondEnemy = battle.Spawn(TestData.MakeCombatant("Second", battle.EnemyFaction), new Vector3I(0, 0, 0));
    var objective = battle.Read(() => battle.Session.GetObjectives(battle.PlayerFaction)[0]);
    battle.ClearEvents();

    battle.ApplyDamage(secondEnemy, 20);
    battle.Read(() => Assert.Equal(ObjectiveResult.Ongoing, objective.State));
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction)));

    battle.ApplyDamage(battle.EnemyUnit, 20);

    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction)));
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
    battle.Mutate(() => battle.Session.AddObjective(battle.PlayerFaction, objective));
    objective.Complete = true;
    battle.ClearEvents();

    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetBattleResultQuery()).RequireRight().Outcome);
    battle.Read(() => Assert.Equal(expectedObjectiveResult, objective.State));
    if (expectedObjectiveResult == ObjectiveResult.Passed)
      battle.Events.EventBefore<SessionEndedBattleEvent, ObjectiveCompletedBattleEvent>();
  }

  [TestCase(TestName = "A silent objective completion records once and leaves the battle running")]
  public void SilentObjectiveCompletionRecordsOnceAndLeavesBattleRunning()
  {
    var player = TestData.MakeFaction("Player");
    using var battle = RunnerFixture(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
    });
    var unit = battle.Unit;
    battle.ClearEvents();

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    battle.Read(() => Assert.True(battle.Session.Outcome.IsNone));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count()));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Count()));
  }

  [TestCase(TestName = "A mid-submission objective win drops remaining composite move steps")]
  public void MidSubmissionObjectiveWinDropsRemainingCompositeMoveSteps()
  {
    var player = TestData.MakeFaction("Player");
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = RunnerFixture(player, new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitMovedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    var unit = battle.Unit;

    battle.Move(unit, [mid, end]);

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
    battle.Read(() => Assert.Equal(mid, battle.Session.GetUnitPosition(unit).RequireSome().Raw));
  }

  [TestCase(TestName = "The player's eliminate-all completes the instant a reaction kills the last enemy during the enemy's turn")]
  public void CrossFactionCompletionResolvesInTheKillingDispatch()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)),
      configure: PlayerObjectives(new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    battle.ClearEvents();

    battle.AdvanceTurn(); // now the enemy's turn
    battle.Read(() => Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide));

    battle.ApplyDamage(battle.EnemyUnit, 999); // a reaction kill during the enemy's own turn

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome)); // resolved NOW, not at turn end
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectiveCompletedBattleEvent>().AsValueEnumerable().Count(e => e.Faction == battle.PlayerFaction)));
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome));
  }

  [TestCase(TestName = "A wiped player loses instantly even with silent directives")]
  public void WipedPlayerLosesWithSilentDirectives()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)),
      configure: PlayerObjectives(new FakeObjectiveData())); // no directives
    battle.ClearEvents();

    battle.ApplyDamage(battle.PlayerUnit, 999);

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome())); // the backstop, not a directive
  }

  [TestCase(DamageKind.Health, TestName = "Directives cannot rescue a wiped player")]
  [TestCase(DamageKind.Stun, TestName = "Directives cannot rescue an unconscious player")]
  public void DirectivesCannotRescueAWipedPlayer(DamageKind kind)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)),
      configure: PlayerObjectives(new EliminateAllOpposingForcesObjectiveData
      {
        OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    battle.ClearEvents();

    battle.ApplyDamage(battle.PlayerUnit, 999, kind); // owner wiped -> backstop Defeat, not the authored rescue directive

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome()));
    battle.Read(() => Assert.Equal(BattleOutcome.Defeat, battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single().Outcome));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<ObjectiveFailedBattleEvent>().AsValueEnumerable().Count()));
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
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5), playerControlled: true,
      player: new("P1", Position: new Vector3I(0, 0, 0)), enemy: new("E1", Position: new Vector3I(2, 0, 0)),
      configure: PlayerObjectives(new EliminateAllOpposingForcesObjectiveData
      {
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      }));
    // Priority 0 fires BEFORE ObjectiveSystem (priority 100): the interrupt is queued,
    // then the objective ends the battle — the guard must drop the queued interrupt.
    battle.RegisterHook<UnitKilledBattleEvent>(new QueueInterruptOnKill(
      BattleAction.PassUnit(battle.Alive(battle.PlayerUnit))), 0);

    battle.ApplyDamage(battle.EnemyUnit, 999); // without the guard this throws (PassUnit requires InProgress)

    battle.Read(() => Assert.True(battle.Session.Outcome.IsSome));
    battle.Read(() => Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome()));
  }
}
