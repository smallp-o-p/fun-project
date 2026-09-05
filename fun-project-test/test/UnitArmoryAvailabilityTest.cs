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
}
