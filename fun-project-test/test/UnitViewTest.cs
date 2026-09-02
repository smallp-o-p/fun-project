using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestFactory;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class UnitViewTest
{
  // Rebuild-in-code pattern (UnitRosterTest precedent): the GdUnit root is the test
  // subproject, so the authored UnitView.tscn cannot be loaded; we pack one in code.
  private static UnitView BuildView()
  {
    var view = AutoFree(new UnitView());

    var margin = new MarginContainer { Name = "MarginContainer" };
    var vbox = new VBoxContainer { Name = "VBoxContainer" };
    var header = new HBoxContainer { Name = "Header" };
    var title = new RichTextLabel { Name = "Title" };
    title.UniqueNameInOwner = true;
    var back = new Button { Name = "BackButton" };
    back.UniqueNameInOwner = true;
    header.AddChild(title);
    header.AddChild(back);

    var body = new HBoxContainer { Name = "Body" };
    var statsColumn = new VBoxContainer { Name = "StatsColumn" };
    var statsList = new VBoxContainer { Name = "StatsList" };
    statsList.UniqueNameInOwner = true;
    var buffsList = new VBoxContainer { Name = "BuffsList" };
    buffsList.UniqueNameInOwner = true;
    var personalMods = new VBoxContainer { Name = "PersonalModSlots" };
    personalMods.UniqueNameInOwner = true;
    statsColumn.AddChild(statsList);
    statsColumn.AddChild(buffsList);
    statsColumn.AddChild(personalMods);

    var equipColumn = new VBoxContainer { Name = "EquipColumn" };
    var weaponSlot = new Button { Name = "WeaponSlot" };
    weaponSlot.UniqueNameInOwner = true;
    var weaponMods = new VBoxContainer { Name = "WeaponModSlots" };
    weaponMods.UniqueNameInOwner = true;
    var armorSlot = new Button { Name = "ArmorSlot" };
    armorSlot.UniqueNameInOwner = true;
    var utilitySlots = new VBoxContainer { Name = "UtilitySlots" };
    utilitySlots.UniqueNameInOwner = true;
    var unequip = new Button { Name = "UnequipButton" };
    unequip.UniqueNameInOwner = true;
    var armoryList = new VBoxContainer { Name = "ArmoryList" };
    armoryList.UniqueNameInOwner = true;
    equipColumn.AddChild(weaponSlot);
    equipColumn.AddChild(weaponMods);
    equipColumn.AddChild(armorSlot);
    equipColumn.AddChild(utilitySlots);
    equipColumn.AddChild(unequip);
    equipColumn.AddChild(armoryList);

    body.AddChild(statsColumn);
    body.AddChild(equipColumn);
    vbox.AddChild(header);
    vbox.AddChild(body);
    margin.AddChild(vbox);
    view.AddChild(margin);
    foreach (Node node in new Node[]
      { title, back, statsList, buffsList, personalMods, weaponSlot, weaponMods, armorSlot, utilitySlots, unequip, armoryList })
      node.Owner = view;

    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
    return view;
  }

  private static GameState MakeStateWithArmory()
    => new(MakeStart(armory: [new ArmoryEntryData { Item = MakeFirearmData("Test Pistol"), Count = 2 }],
      modStock: [new ModStockEntryData { Mod = new MultiStatMod { Name = "Reflex Chip" }, Count = 1 }]));

  [TestCase(TestName = "Present renders title and base->effective stats")]
  public void PresentRendersStats()
  {
    var view = BuildView();
    Combatant unit = new(MakeCombatantData("Mold"), new Faction(new FactionData()), Some("Cpl. Ada Voss"));
    unit.GetModSlots()[0].Equip(new MultiStatMod
    {
      Name = "Reflex Chip",
      StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(10)] }],
    });

    view.Present(MakeStateWithArmory(), unit);

    Assert.Equal("Cpl. Ada Voss", view.GetNode<RichTextLabel>("%Title").Text);
    var stats = view.GetNode<VBoxContainer>("%StatsList");
    Assert.Equal(6, stats.GetChildCount());
    Assert.Equal("Aim: 60 -> 70", ((RichTextLabel)stats.GetChild(5)).Text);
  }

  [TestCase(TestName = "Present renders innate buffs and personal mod slots")]
  public void PresentRendersBuffsAndMods()
  {
    var view = BuildView();
    Combatant unit = new(MakeCombatantData("Mold"), new Faction(new FactionData()));
    unit.GetModSlots()[0].Equip(new MultiStatMod { Name = "Reflex Chip" });

    view.Present(MakeStateWithArmory(), unit);

    Assert.Equal(2, view.GetNode<VBoxContainer>("%PersonalModSlots").GetChildCount());
    AssertThat(view.GetNode<VBoxContainer>("%PersonalModSlots").GetChild<Button>(0).Text).Contains("Reflex Chip");
  }

  [TestCase(TestName = "Present renders weapon slot summary with ammo for magazine weapons")]
  public void PresentRendersWeaponSummary()
  {
    var view = BuildView();
    Combatant unit = new(MakeCombatantData("Mold"), new Faction(new FactionData()));
    unit.EquipWeapon(ItemRuntimeFactory.CreateWeapon(MakeFirearmData("Test Pistol")));

    view.Present(MakeStateWithArmory(), unit);

    string text = view.GetNode<Button>("%WeaponSlot").Text;
    AssertThat(text).Contains("Test Pistol");
    AssertThat(text).Contains("AMMO");
  }

  [TestCase(TestName = "Back button invokes the armed close request")]
  public void BackButtonInvokesArmedClose()
  {
    var view = BuildView();
    bool requested = false;
    view.ArmClose(() => requested = true);

    view.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);

    Assert.True(requested);
  }
}
