using System.Threading.Tasks;
using Godot;
using FunProject.Engineering;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class UnitArmoryAvailabilityTest
{
  [TestCase(UnitViewSlotKind.Weapon, "Rifle")]
  [TestCase(UnitViewSlotKind.Armor, "Vest")]
  [TestCase(UnitViewSlotKind.Utility, "Field Kit")]
  public static async Task EquipmentBrowserOffersOnlyCompatibleItems(UnitViewSlotKind kind, string expected)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()],
      armory: [MakeWeaponData(name: "Rifle"), MakeArmorData("Vest"),
        MakeItemData("Workshop Sample"), MakeUtilityData("Field Kit")]));
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, campaign.State.Roster[0]);

    view.SelectSlot(new UnitViewSlot(kind));

    var choices = view.GetNode<VBoxContainer>("%ArmoryList");
    Assert.Equal(1, choices.GetChildCount());
    Assert.True(choices.GetChild<Button>(0).Text.Contains(expected));
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

  [TestCase(false)]
  [TestCase(true)]
  public static async Task UnitBrowserTracksAvailableSupply(bool unlimited)
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
  public static async Task ActivationRefreshesTheRetainedUnitWithoutClearingSlotSelection()
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
