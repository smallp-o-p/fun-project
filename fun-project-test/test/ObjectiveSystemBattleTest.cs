using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveSystemBattleTest
{
  [TestCase(TestName = "An authored objective is the current objective after StartBattle")]
  public void AuthoredObjectiveSuppressesDefault()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var b = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a, b]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", b), new Vector3I(2, 0, 0));
    session.AddObjective(a, new FakeObjective());
    StartBattle(session);

    Operation op = session.GetOperation(a).RequireSome();
    Assert.True(op.Current.RequireSome() is FakeObjective);
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

  [TestCase(TestName = "An operation that completes at the owner's turn end raises completion events")]
  public void OperationCompletesAtTurnEnd()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var b = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a, b]); // no player faction
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", b), new Vector3I(2, 0, 0));
    session.AddObjective(a, new FakeObjective { Complete = true });
    var recorder = new BattleEventRecorder(session);
    StartBattle(session);

    AdvanceTurn(session);

    Assert.Equal(1, recorder.OfType<ObjectiveCompletedBattleEvent>().Count());
    Assert.Equal(1, recorder.OfType<OperationCompletedBattleEvent>().Count());
    Assert.Equal(OperationStatus.Completed, session.GetOperation(a).RequireSome().Status);
    Assert.Equal(BattlePhase.InProgress, session.Phase); // no player faction => battle keeps going
  }

  [TestCase(TestName = "An operation that fails at the owner's turn end raises failure events")]
  public void OperationFailsAtTurnEnd()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var b = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a, b]); // no player faction
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", b), new Vector3I(2, 0, 0));
    session.AddObjective(a, new FakeObjective { Failed = true });
    var recorder = new BattleEventRecorder(session);
    StartBattle(session);

    AdvanceTurn(session);

    Assert.Equal(1, recorder.OfType<ObjectiveFailedBattleEvent>().Count());
    Assert.Equal(1, recorder.OfType<OperationFailedBattleEvent>().Count());
    Assert.Equal(OperationStatus.Failed, session.GetOperation(a).RequireSome().Status);
  }

  [TestCase(TestName = "GetOperationForFaction returns the faction's operation")]
  public void QueryReturnsOperation()
  {
    var a = BattleTestFactory.MakeFaction("A");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [a]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", a), new Vector3I(0, 0, 0));
    StartBattle(session);

    Option<Operation> result = Query(session, new GetOperationForFaction(a));

    Assert.True(result.IsSome);
  }
}
