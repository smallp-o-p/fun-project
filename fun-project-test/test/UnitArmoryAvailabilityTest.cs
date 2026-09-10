using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeUiTestFactory;

[TestSuite]
[RequireGodotRuntime]
public class UnitArmoryAvailabilityTest
{
  [TestCase(false)]
  [TestCase(true)]
  public void UnitBrowserTracksAvailableSupply(bool unlimited)
  {
    var item = MakeItemData(unlimited: unlimited);
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()], manufacturableItems: [item]));
    var state = campaign.State;
    var view = AddToTree(CreateUnitView());
    view.Present(state, state.Roster[0]);
    view.SelectSlot(UnitViewSlot.Utility(0));
    Assert.Equal(0, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());

    campaign.Session.StartManufacturing(item).RequireRight();
    campaign.AdvanceTicks(1440);

    view.Present(state, state.Roster[0]);
    view.SelectSlot(UnitViewSlot.Utility(0));
    Assert.Equal(1, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());
    state.Armory.TryWithdrawItem(item).RequireSome();
    view.Present(state, state.Roster[0]);
    view.SelectSlot(UnitViewSlot.Utility(0));
    Assert.Equal(unlimited ? 1 : 0, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());
  }

  // Activation (returning from a covered view) refreshes the retained unit while keeping
  // the selected slot: the weapon filter survives and reflects stock that moved meanwhile.
  [TestCase]
  public void ActivationRefreshesTheRetainedUnitWithoutClearingSlotSelection()
  {
    var rifle = MakeWeaponData(name: "Rifle");
    var cannon = MakeWeaponData(name: "Cannon");
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()], armory: [rifle, cannon, MakeItemData("Kit")]));
    var state = campaign.State;
    var view = AddToTree(CreateUnitView());
    view.BindUnit(state.Roster[0]);
    view.Present(state, campaign.Session);
    view.SelectSlot(UnitViewSlot.Weapon);
    var armory = view.GetNode<VBoxContainer>("%ArmoryList");
    Assert.Equal(2, armory.GetChildCount()); // weapon slot: the two guns, not the kit

    state.Armory.TryWithdrawItem(rifle).RequireSome(); // domain moves while covered

    view.Present(state, campaign.Session); // reactivation reruns the refresh
    Assert.Equal(1, armory.GetChildCount()); // refreshed AND still weapon-filtered
  }
}
