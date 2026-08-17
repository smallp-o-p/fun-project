using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class UseItemActionTest
{
  [TestCase(TestName = "UseItem spends one charge and default AP while charges remain")]
  public void UseItemSpendsOneChargeAndDefaultApWhileChargesRemain()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1), actionPoints: 4);
    var usable = BattleTestFactory.MakeUsableItem("Medkit", maxCharges: 2);
    unit.AddInventoryItem(usable.Item);
    var recorder = new BattleEventRecorder(session);

    var result = executor.Submit(BattleAction.UseItem(unit.AliveIn(session), usable));

    Assert.True(unit.HasInventoryItem(usable.Item));
    Assert.Equal(1, usable.Capability.Current);
    Assert.Equal(4 - BattleSession.DefaultUseItemActionPointCost, unit.CurrentActionPoints);
    var usedEvent = recorder.Single<ItemUsedBattleEvent>();
    Assert.True(ReferenceEquals(unit.State, usedEvent.Unit));
    Assert.True(ReferenceEquals(usable.Item, usedEvent.Item));
  }

  [TestCase(TestName = "UseItem removes the item when the last charge is spent")]
  public void UseItemRemovesTheItemWhenTheLastChargeIsSpent()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1));
    var usable = BattleTestFactory.MakeUsableItem("Stim", maxCharges: 1);
    unit.AddInventoryItem(usable.Item);

    executor.Submit(BattleAction.UseItem(unit.AliveIn(session), usable));

    Assert.False(unit.HasInventoryItem(usable.Item));
  }

  [TestCase(TestName = "Using a depleted item is interrupted without spending AP or raising the event")]
  public void UsingADepletedItemIsInterruptedWithoutSpendingAp()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1), actionPoints: 4);
    var usable = BattleTestFactory.MakeUsableItem("Flare", maxCharges: 1);
    unit.AddInventoryItem(usable.Item);
    executor.Submit(BattleAction.UseItem(unit.AliveIn(session), usable));
    int apAfterFirstUse = unit.CurrentActionPoints;
    // The item was removed with the last charge; re-adding the same depleted instance
    // simulates a caller that failed to check before constructing the action. Execution
    // re-checks depletion — the same check that protects interleaved interrupts — and
    // interrupts quietly: no charge spent, no event, no AP.
    unit.AddInventoryItem(usable.Item);
    var recorder = new BattleEventRecorder(session);

    executor.Submit(BattleAction.UseItem(unit.AliveIn(session), usable));

    Assert.Equal(0, usable.Capability.Current);
    Assert.Equal(apAfterFirstUse, unit.CurrentActionPoints);
    Assert.False(recorder.OfType<ItemUsedBattleEvent>().AsValueEnumerable().Any());
  }
}
