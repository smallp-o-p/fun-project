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
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1), actionPoints: 4);
    var unit = battle.Unit;
    var usable = TestData.MakeUsableItem("Medkit", maxCharges: 2);
    unit.AddInventoryItem(usable.Item);
    battle.ClearEvents();

    var result = battle.Use(unit, usable);

    Assert.True(unit.HasInventoryItem(usable.Item));
    Assert.Equal(1, usable.Capability.Current);
    Assert.Equal(4 - BattleSession.DefaultUseItemActionPointCost, unit.CurrentActionPoints);
    var usedEvent = battle.Events.SingleEvent<ItemUsedBattleEvent>();
    Assert.True(ReferenceEquals(unit, usedEvent.Unit));
    Assert.True(ReferenceEquals(usable.Item, usedEvent.Item));
  }

  [TestCase(TestName = "UseItem removes the item when the last charge is spent")]
  public void UseItemRemovesTheItemWhenTheLastChargeIsSpent()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1));
    var unit = battle.Unit;
    var usable = TestData.MakeUsableItem("Stim", maxCharges: 1);
    unit.AddInventoryItem(usable.Item);

    battle.Use(unit, usable);

    Assert.False(unit.HasInventoryItem(usable.Item));
  }

  [TestCase(TestName = "Using a depleted item is interrupted without spending AP or raising the event")]
  public void UsingADepletedItemIsInterruptedWithoutSpendingAp()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1), actionPoints: 4);
    var unit = battle.Unit;
    var usable = TestData.MakeUsableItem("Flare", maxCharges: 1);
    unit.AddInventoryItem(usable.Item);
    battle.Use(unit, usable);
    int apAfterFirstUse = unit.CurrentActionPoints;
    // The item was removed with the last charge; re-adding the same depleted instance
    // simulates a caller that failed to check before constructing the action. Execution
    // re-checks depletion — the same check that protects interleaved interrupts — and
    // interrupts quietly: no charge spent, no event, no AP.
    unit.AddInventoryItem(usable.Item);
    battle.ClearEvents();

    battle.Use(unit, usable);

    Assert.Equal(0, usable.Capability.Current);
    Assert.Equal(apAfterFirstUse, unit.CurrentActionPoints);
    Assert.False(battle.Events.EventsOf<ItemUsedBattleEvent>().AsValueEnumerable().Any());
  }
}
