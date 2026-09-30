using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveSystemBattleTest
{
  [TestCase(TestName = "Objectives added after executor construction are read from session state")]
  public void PostExecutorObjectivesAreReadFromSessionState()
  {
    var a = TestData.MakeFaction("A");
    var b = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [a, b]);
    battle.Spawn(TestData.MakeCombatant("A1", a), new Vector3I(0, 0, 0)); // the fixture's executor exists from construction
    var bUnit = battle.Spawn(TestData.MakeCombatant("B1", b), new Vector3I(2, 0, 0));
    // The fixture already constructed the executor (and its ObjectiveSystem); the objective
    // is read from session state when the later kill event is routed.
    battle.Session.AddObjective(a, new FakeObjective { Complete = true });
    battle.Start();

    battle.ApplyDamage(bUnit, 999);

    Assert.Equal(1, battle.Session.GetObjectives(a).AsValueEnumerable().Count(o => o.State == ObjectiveResult.Passed));
  }

  [TestCase(TestName = "Objectives present before executor construction are read from session state")]
  public void PreExecutorObjectivesAreReadFromSessionState()
  {
    var a = TestData.MakeFaction("A");
    var b = TestData.MakeFaction("B");
    var session = new BattleSession(new BattleBoardState(new Vector3I(5, 1, 5)), [a, b]);
    // No executor yet. The objective remains in session state and is visible to the
    // catch-all router once the executor is constructed.
    session.AddObjective(a, new FakeObjective { Complete = true });

    using var executor = new BattleActionExecutor(session);
    executor.Submit(BattleAction.SpawnUnit(TestData.MakeCombatant("A1", a), session.Board.At(new Vector3I(0, 0, 0))));
    executor.Submit(BattleAction.SpawnUnit(TestData.MakeCombatant("B1", b), session.Board.At(new Vector3I(2, 0, 0))));
    var bUnit = session.GetUnitAt(session.Board.At(new Vector3I(2, 0, 0))).RequireSome();
    // The old setup helper's missing-objective policy, spelled out: faction B has no
    // authored objective, so it gets the silent eliminate-all default (no player faction
    // means no Victory directive) before the battle can start.
    session.AddObjective(b, new EliminateAllOpposingForcesObjectiveData().Instantiate());
    executor.Submit(BattleAction.StartBattle());

    executor.Submit(BattleAction.ApplyDamage(session.TryGetAlive(bUnit).RequireSome(), 999));

    Assert.Equal(1, session.GetObjectives(a).AsValueEnumerable().Count(o => o.State == ObjectiveResult.Passed));
  }

  [TestCase(TestName = "AddObjective raises an ObjectiveAdded event")]
  public void AddObjectiveRaisesEvent()
  {
    var a = TestData.MakeFaction("A");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [a]);
    battle.Spawn(TestData.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    battle.ClearEvents();

    battle.Session.AddObjective(a, new FakeObjective());

    Assert.Equal(1, battle.Events.EventsOf<ObjectiveAddedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "An objective is only checked against events it observes")]
  public void ObjectivesAreOnlyCheckedAgainstObservedEvents()
  {
    var a = TestData.MakeFaction("A");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [a]);
    var unit = battle.Spawn(TestData.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    var objective = new FakeObjective { Observe = typeof(UnitKilledBattleEvent) };
    battle.Session.AddObjective(a, objective);
    battle.Start();

    battle.ApplyDamage(unit, 1); // UnitDamaged — not observed
    battle.Pass(unit);           // UnitActivationEnded — not observed
    Assert.Equal(0, objective.CheckCount);

    battle.ApplyDamage(unit, 999); // UnitKilled — observed
    Assert.Equal(1, objective.CheckCount);
  }

  [TestCase(TestName = "A queued follow-up is visible starting with the next event")]
  public void QueuedFollowUpRoutesFromNextEvent()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var main = new FakeObjectiveData
    {
      OnFail = new QueueDirectiveData
      {
        FollowUps =
        [
          new SurviveUntilTurnObjectiveData
          {
            TargetTurn = 2,
            OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
          },
        ],
      },
    };
    battle.Session.AddObjective(player, new FakeObjective(main) { Failed = true });
    battle.Start();

    battle.ApplyDamage(enemyUnit, 999); // main fails on the kill; follow-up queued on the SAME dispatch

    // The follow-up must NOT have been checked against the killing event: candidates were
    // snapshotted before the directive added it. Its target is TurnStarted anyway, so the
    // battle continues...
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);

    battle.AdvanceTurn(); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> follow-up completes -> Defeat

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
  }

  [TestCase(false, TestName = "Flips resolve in add order and the first EndBattle wins")]
  [TestCase(true, TestName = "Add order wins across concrete and interface observed keys")]
  public void AddOrderWinsAcrossObservedKeyBuckets(bool interfaceKey)
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = battle.Spawn(TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    // The interface-key row proves cross-bucket matching and add-order traversal; the null
    // Observe falls back to FakeObjective's concrete UnitKilled key.
    var first = new FakeObjective(new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    })
    {
      Complete = true,
      Observe = interfaceKey ? typeof(IUnitBattleEvent) : null,
    };
    var second = new FakeObjective(new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    })
    {
      Complete = true,
      Observe = typeof(UnitKilledBattleEvent),
    };
    battle.Session.AddObjective(player, first);
    battle.Session.AddObjective(player, second);
    battle.Start();

    battle.ApplyDamage(enemyUnit, 999); // both flip on the same kill; add order wins

    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
  }

  [TestCase(TestName = "GetObjectivesForFaction returns the faction's objectives")]
  public void QueryReturnsObjectives()
  {
    var a = TestData.MakeFaction("A");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [a]);
    battle.Spawn(TestData.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    battle.Session.AddObjective(a, new FakeObjective());
    battle.Start();

    Option<IReadOnlyList<Objective>> result = battle.Query(new GetObjectivesForFaction(a));

    Assert.True(result.IsSome);
    Assert.Equal(1, result.RequireSome().Count);
  }
}
