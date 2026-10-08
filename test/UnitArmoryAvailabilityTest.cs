using System.Threading.Tasks;
using Godot;
using FunProject.Engineering;
using FunProject.Items;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class UnitArmoryAvailabilityTest
{
  [TestCase]
  public static async Task UtilityBrowserOffersOnlyUtilityEquipment()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var sample = MakeItemData("Workshop Sample");
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()],
      armory: [MakeWeaponData(name: "Rifle"), MakeArmorData("Vest"),
        sample, MakeUtilityData("Field Kit")]));
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, campaign.State.Roster[0]);

    view.SelectSlot(UnitViewSlot.Utility(0));

    var choices = view.GetNode<VBoxContainer>("%ArmoryList");
    Assert.Equal(1, choices.GetChildCount());
    Assert.True(choices.GetChild<Button>(0).Text.Contains("Field Kit"));
    choices.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Field Kit", campaign.State.Roster[0].Inventory[0].Item.ItemName);
    Assert.Equal(1, campaign.State.Armory.TryGetItemStock(sample).RequireSome().Remaining);
  }

  [TestCase]
  public static async Task ChangingSlotsRejectsAnOldBrowserChoiceBeforeWithdrawal()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var rifle = MakeWeaponData(name: "Rifle");
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()], armory: [rifle, MakeUtilityData("Kit")]));
    var unit = campaign.State.Roster[0];
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, unit);
    var oldWeaponChoice = ArmoryButton(view, "Rifle");
    view.SelectSlot(UnitViewSlot.Utility(0));
    ArmoryButton(view, "Kit").EmitSignal(Button.SignalName.Pressed);

    oldWeaponChoice.EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("Kit", unit.Inventory[0].Item.ItemName);
    Assert.True(unit.EquippedWeapon.IsNone);
    Assert.Equal(1, campaign.State.Armory.TryGetItemStock(rifle).RequireSome().Remaining);
  }

  [TestCase(UnitViewSlotKind.Weapon)]
  [TestCase(UnitViewSlotKind.Armor)]
  public static async Task UtilityMarkerDoesNotReplaceWeaponOrArmorRequirements(UnitViewSlotKind kind)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var bare = MakeUtilityData("Utility item");
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()], armory: [bare]));
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, campaign.State.Roster[0]);

    view.SelectSlot(new UnitViewSlot(kind));

    Assert.Equal(0, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task UnitBrowserTracksAvailableSupply(bool unlimited)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeUtilityData(unlimited: unlimited);
    var itemRecipe = MakeManufacturingRecipe(item);
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()], manufacturingRecipes: [itemRecipe]));
    var state = campaign.State;
    var view = AddToTree(CreateUnitView());
    view.Present(state, state.Roster[0]);
    view.SelectSlot(UnitViewSlot.Utility(0));
    Assert.Equal(0, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());

    campaign.Session.StartManufacturing(itemRecipe).RequireRight();
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
  public async Task ActivationRefreshesTheRetainedUnitWithoutClearingSlotSelection()
  {
    await using var cleanup = new DeferredNodeCleanup();
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
