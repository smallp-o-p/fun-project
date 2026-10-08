using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveSystemBattleTest
{
  private static BattleFixture SoloFixture(Faction faction, params ObjectiveData[] objectives)
    => new(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(5, 1, 5)),
      [new BattleSideSetup(faction, objectives,
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A1", faction)), new Vector3I(0, 0, 0))])],
      Seed: 7));

  private static BattleFixture DuelFixture(Faction player, Faction enemy, Vector3I enemyPosition,
    params ObjectiveData[] playerObjectives)
    => new(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(5, 1, 5)),
      [
        new BattleSideSetup(player, playerObjectives,
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("P1", player)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemy, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("E1", enemy)), enemyPosition)]),
      ],
      Seed: 7,
      PlayerFaction: Some(player)));

  [TestCase(TestName = "Objectives added after executor construction are read from session state")]
  public void PostExecutorObjectivesAreReadFromSessionState()
  {
    var a = TestData.MakeFaction("A");
    var b = TestData.MakeFaction("B");
    using var battle = DuelFixture(a, b, new Vector3I(2, 0, 0), [new FakeObjectiveData()]);
    var bUnit = battle.UnitAt(new Vector3I(2, 0, 0));
    // The fixture's executor (and its ObjectiveSystem) already exists from construction;
    // the objective is read from session state when the later kill event is routed.
    battle.Session.AddObjective(a, new FakeObjective { Complete = true });

    battle.ApplyDamage(bUnit, 999);

    Assert.Equal(1, battle.Session.GetObjectives(a).AsValueEnumerable().Count(o => o.State == ObjectiveResult.Passed));
  }

  [TestCase(TestName = "AddObjective raises an ObjectiveAdded event")]
  public void AddObjectiveRaisesEvent()
  {
    var a = TestData.MakeFaction("A");
    using var battle = SoloFixture(a, new FakeObjectiveData());
    battle.ClearEvents();

    battle.Session.AddObjective(a, new FakeObjective());

    Assert.Equal(1, battle.Events.EventsOf<ObjectiveAddedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "An objective is only checked against events it observes")]
  public void ObjectivesAreOnlyCheckedAgainstObservedEvents()
  {
    var a = TestData.MakeFaction("A");
    using var battle = SoloFixture(a, new EliminateAllOpposingForcesObjectiveData());
    var unit = battle.Unit;
    var objective = new FakeObjective { Observe = typeof(UnitKilledBattleEvent) };
    battle.Session.AddObjective(a, objective);

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
    var main = new FakeObjectiveData
    {
      Failed = true,
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
    using var battle = DuelFixture(player, enemy, new Vector3I(2, 0, 0), main);
    var enemyUnit = battle.UnitAt(new Vector3I(2, 0, 0));

    battle.ApplyDamage(enemyUnit, 999); // main fails on the kill; follow-up queued on the SAME dispatch

    Assert.Equal(2, battle.Session.GetObjectives(player).Count);
    Assert.Equal(ObjectiveResult.Failed, battle.Session.GetObjectives(player)[0].State);
    Assert.Equal(ObjectiveResult.Ongoing, battle.Session.GetObjectives(player)[1].State);

    // The follow-up must NOT have been checked against the killing event: candidates were
    // snapshotted before the directive added it. Its target is TurnStarted anyway, so the
    // battle continues...
    Assert.True(battle.Session.Outcome.IsNone);

    battle.AdvanceTurn(); // enemy turn 1 ends -> round rolls -> TurnStarted(player, 2) -> follow-up completes -> Defeat

    Assert.True(battle.Session.Outcome.IsSome);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
  }

  [TestCase(false, TestName = "Flips resolve in add order and the first EndBattle wins")]
  [TestCase(true, TestName = "Add order wins across concrete and interface observed keys")]
  public void AddOrderWinsAcrossObservedKeyBuckets(bool interfaceKey)
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    // The interface-key row proves cross-bucket matching and add-order traversal; the null
    // Observe falls back to FakeObjective's concrete UnitKilled key. The objectives flip on
    // the kill event, so they arm after startup as runtime objectives in add order — an
    // interface-key objective authored in the setup would complete during startup itself.
    using var battle = DuelFixture(player, enemy, new Vector3I(2, 0, 0), new FakeObjectiveData());
    var enemyUnit = battle.UnitAt(new Vector3I(2, 0, 0));
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

    battle.ApplyDamage(enemyUnit, 999); // both flip on the same kill; add order wins

    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
  }

  [TestCase(TestName = "GetObjectivesForFaction returns the faction's objectives")]
  public void QueryReturnsObjectives()
  {
    var a = TestData.MakeFaction("A");
    using var battle = SoloFixture(a, new FakeObjectiveData());

    Option<IReadOnlyList<Objective>> result = battle.Query(new GetObjectivesForFaction(a));

    Assert.True(result.IsSome);
    Assert.Equal(1, result.RequireSome().Count);
  }
}
