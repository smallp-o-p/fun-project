using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveSystemBattleTest
{
  [TestCase(TestName = "Objectives added after executor construction are read from session state")]
  public void PostExecutorObjectivesAreReadFromSessionState()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var b = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a, b]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0)); // creates the executor
    var bUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", b), new Vector3I(2, 0, 0));
    // SpawnUnit already constructed the executor (and its ObjectiveSystem); the objective
    // is read from session state when the later kill event is routed.
    session.AddObjective(a, new FakeObjective { Complete = true });
    StartBattle(session);

    ApplyDamage(session, bUnit, 999);

    Assert.Equal(1, session.GetObjectives(a).Count(o => o.State == ObjectiveResult.Passed));
  }

  [TestCase(TestName = "Objectives present before executor construction are read from session state")]
  public void PreExecutorObjectivesAreReadFromSessionState()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var b = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a, b]);
    // No SpawnUnit yet — no executor yet. The objective remains in session state and is
    // visible to the catch-all router once the executor is constructed.
    session.AddObjective(a, new FakeObjective { Complete = true });

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0)); // now the executor exists
    var bUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", b), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, bUnit, 999);

    Assert.Equal(1, session.GetObjectives(a).Count(o => o.State == ObjectiveResult.Passed));
  }

  [TestCase(TestName = "AddObjective raises an ObjectiveAdded event")]
  public void AddObjectiveRaisesEvent()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    var recorder = new BattleEventRecorder(session);

    session.AddObjective(a, new FakeObjective());

    Assert.Equal(1, recorder.OfType<ObjectiveAddedBattleEvent>().Count());
  }

  [TestCase(TestName = "An objective is only checked against events it observes")]
  public void ObjectivesAreOnlyCheckedAgainstObservedEvents()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    var objective = new FakeObjective { Observe = typeof(UnitKilledBattleEvent) };
    session.AddObjective(a, objective);
    StartBattle(session);

    ApplyDamage(session, unit, 1); // UnitDamaged — not observed
    PassUnit(session, unit.State); // UnitActivationEnded — not observed
    Assert.Equal(0, objective.CheckCount);

    ApplyDamage(session, unit, 999); // UnitKilled — observed
    Assert.Equal(1, objective.CheckCount);
  }

  [TestCase(TestName = "A queued follow-up is visible starting with the next event")]
  public void QueuedFollowUpRoutesFromNextEvent()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
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
    session.AddObjective(player, new FakeObjective(main) { Failed = true });
    StartBattle(session);

    ApplyDamage(session, enemyUnit, 999); // main fails on the kill; follow-up queued on the SAME dispatch

    // The follow-up must NOT have been checked against the killing event: candidates were
    // snapshotted before the directive added it. Its target is TurnStarted anyway, so the
    // battle continues...
    Assert.Equal(BattlePhase.InProgress, session.Phase);

    AdvanceTurn(session); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> follow-up completes -> Defeat

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "Flips resolve in add order and the first EndBattle wins")]
  public void FirstEndBattleWins()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new FakeObjective(new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    })
    { Complete = true });
    session.AddObjective(player, new FakeObjective(new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    })
    { Complete = true });
    StartBattle(session);

    ApplyDamage(session, enemyUnit, 999); // both flip on the same kill; add order wins

    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "Add order wins across concrete and interface observed keys")]
  public void AddOrderWinsAcrossObservedKeyBuckets()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var first = new FakeObjective(new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    })
    {
      Complete = true,
      Observe = typeof(IUnitBattleEvent),
    };
    var second = new FakeObjective(new FakeObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    })
    {
      Complete = true,
      Observe = typeof(UnitKilledBattleEvent),
    };
    session.AddObjective(player, first);
    session.AddObjective(player, second);
    StartBattle(session);

    ApplyDamage(session, enemyUnit, 999);

    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "GetObjectivesForFaction returns the faction's objectives")]
  public void QueryReturnsObjectives()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    session.AddObjective(a, new FakeObjective());
    StartBattle(session);

    Option<IReadOnlyList<Objective>> result = Query(session, new GetObjectivesForFaction(a));

    Assert.True(result.IsSome);
    Assert.Equal(1, result.RequireSome().Count);
  }
}
