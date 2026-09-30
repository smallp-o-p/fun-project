using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class UseItemActionTest
{
  [TestCase("Medkit", 2, true, 1, TestName = "a Medkit with two charges keeps one after use")]
  [TestCase("Stim", 1, false, 0, TestName = "a one-charge Stim is removed with its last charge")]
  public void SuccessfulUse(string item, int maxCharges, bool remainsInInventory, int expectedCurrent)
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(1, 0, 1), actionPoints: 4);
    var unit = battle.Unit;
    var usable = TestData.MakeUsableItem(item, maxCharges: maxCharges);
    unit.AddInventoryItem(usable.Item);
    battle.ClearEvents();

    battle.Use(unit, usable);

    Assert.Equal(remainsInInventory, unit.HasInventoryItem(usable.Item));
    Assert.Equal(expectedCurrent, usable.Capability.Current);
    Assert.Equal(4 - BattleSession.DefaultUseItemActionPointCost, unit.CurrentActionPoints);
    var usedEvent = battle.Events.SingleEvent<ItemUsedBattleEvent>();
    Assert.True(ReferenceEquals(unit, usedEvent.Unit));
    Assert.True(ReferenceEquals(usable.Item, usedEvent.Item));
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
