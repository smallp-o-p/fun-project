using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class OperationTest
{
  [TestCase(TestName = "Adding an objective sets its owner and makes it current")]
  public void AddObjectiveSetsOwnerAndCurrent()
  {
    var faction = BattleTestFactory.MakeFaction("A");
    var op = new Operation(faction);
    var objective = new FakeObjective();

    op.AddObjective(objective);

    Assert.Equal(OperationStatus.Active, op.Status);
    Assert.Equal(faction, objective.Owner);
    Assert.Equal(objective, op.Current.RequireSome());
  }

  [TestCase(TestName = "Completing the current objective advances, then completes the operation")]
  public void CompletingAdvancesThenCompletes()
  {
    var op = new Operation(BattleTestFactory.MakeFaction("A"));
    var first = new FakeObjective();
    var second = new FakeObjective();
    op.AddObjective(first);
    op.AddObjective(second);

    op.CompleteCurrent();
    Assert.Equal(OperationStatus.Active, op.Status);
    Assert.Equal(second, op.Current.RequireSome());

    op.CompleteCurrent();
    Assert.Equal(OperationStatus.Completed, op.Status);
    Assert.True(op.Current.IsNone);
  }

  [TestCase(TestName = "Failing the current objective halts the operation")]
  public void FailingHaltsTheOperation()
  {
    var op = new Operation(BattleTestFactory.MakeFaction("A"));
    op.AddObjective(new FakeObjective());

    op.FailCurrent();

    Assert.Equal(OperationStatus.Failed, op.Status);
    Assert.True(op.Current.IsNone);
  }

  [TestCase(TestName = "Adding an objective to a failed operation reactivates it")]
  public void AddingToAFailedOperationReactivatesIt()
  {
    var op = new Operation(BattleTestFactory.MakeFaction("A"));
    op.AddObjective(new FakeObjective());
    op.FailCurrent();
    Assert.Equal(OperationStatus.Failed, op.Status);

    var exfiltrate = new FakeObjective();
    op.AddObjective(exfiltrate);

    Assert.Equal(OperationStatus.Active, op.Status);
    Assert.Equal(exfiltrate, op.Current.RequireSome());
  }
}
