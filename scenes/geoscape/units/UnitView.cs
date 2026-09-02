using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;
using System;

// Full-screen soldier screen: stats page (base -> effective), equipment slots, and the
// campaign armory browser. Presentation only — every interaction calls domain operations
// on GameState/Combatant/Armory and re-presents; nothing is cached beyond the bound unit.
public sealed partial class UnitView : PanelContainer, IGeoscapeView
{
  private Action? _requestClose;
  private CampaignGameState? _state;
  private Combatant? _unit;

  public void ArmClose(Action requestClose) => _requestClose = requestClose;

  public override void _Ready()
  {
    GetNode<Button>("%BackButton").Pressed += () => _requestClose?.Invoke();
  }

  public void Present(CampaignGameState state, Combatant unit)
  {
    _state = state;
    _unit = unit;
    RebuildAll();
  }

  private void RebuildAll()
  {
    if (_state is null || _unit is null)
      return;

    GetNode<RichTextLabel>("%Title").Text = _unit.Name;
    RebuildStats();
    RebuildEquipment();
    RebuildBrowser();
  }

  private void RebuildStats()
  {
    var stats = GetNode<VBoxContainer>("%StatsList");
    ClearChildren(stats);
    AddStatRow<HealthStat>("Health");
    AddStatRow<ActionPointsStat>("Action Points");
    AddStatRow<WillStat>("Will");
    AddStatRow<MovementStat>("Movement");
    AddStatRow<VisionStat>("Vision");
    AddStatRow<AimStat>("Aim");

    var buffs = GetNode<VBoxContainer>("%BuffsList");
    ClearChildren(buffs);
    foreach (BuffData buff in _unit!.InnateBuffs)
      AddLabelRow(buffs, buff.Name);
    if (buffs.GetChildCount() == 0)
      AddLabelRow(buffs, "—");
  }

  private void AddStatRow<TStat>(string label) where TStat : Stat
  {
    int baseValue = Mathf.RoundToInt(_unit!.GetStat<TStat>().BaseValue);
    int effective = Mathf.RoundToInt(_unit.Resolve<TStat>(_unit.StatContributions()));
    AddLabelRow(GetNode<VBoxContainer>("%StatsList"), $"{label}: {baseValue} -> {effective}");
  }

  private void RebuildEquipment()
  {
    RebuildPersonalMods();
    RebuildWeaponSlot();
    RebuildArmorSlot();
    RebuildUtilitySlots();
  }

  private void RebuildPersonalMods()
  {
    var slots = GetNode<VBoxContainer>("%PersonalModSlots");
    ClearChildren(slots);
    foreach (ModSlot slot in _unit!.GetModSlots())
      AddSlotButton(slots, SlotLabel(slot));
  }

  private void RebuildWeaponSlot()
  {
    var weaponMods = GetNode<VBoxContainer>("%WeaponModSlots");
    ClearChildren(weaponMods);
    GetNode<Button>("%WeaponSlot").Text = _unit!.EquippedWeapon.Match(
      Some: weapon =>
      {
        foreach (ModSlot slot in weapon.GetModSlots())
          AddSlotButton(weaponMods, SlotLabel(slot));
        return $"Weapon: {WeaponSummary(weapon)}";
      },
      None: () => "Weapon: — empty —");
  }

  private static string WeaponSummary(Weapon weapon)
  {
    string ammo = weapon is AmmunitionedWeapon magazine
      ? $"  AMMO {magazine.CurrentAmmo}/{magazine.MagazineSize}"
      : "";
    return $"{weapon.ItemName}  DMG {weapon.GetDamageStat().BaseValue}  RNG {weapon.EffectiveRange}  "
      + $"CRIT {weapon.GetCritChanceStat().BaseValue}{ammo}";
  }

  private void RebuildArmorSlot()
  {
    GetNode<Button>("%ArmorSlot").Text = _unit!.EquippedArmor.Match(
      Some: armor => $"Armor: {armor.Item.ItemName}  ARMOR {armor.Capability.Max}",
      None: () => "Armor: — empty —");
  }

  private void RebuildUtilitySlots()
  {
    var slots = GetNode<VBoxContainer>("%UtilitySlots");
    ClearChildren(slots);
    for (int i = 0; i < _unit!.MaxInventorySize; i++)
    {
      string text = _unit.Inventory.TryGetValue(i, out EquippableItem? item)
        ? $"Utility {i + 1}: {item.ItemName}"
        : $"Utility {i + 1}: — empty —";
      AddSlotButton(slots, text);
    }
  }

  private static string SlotLabel(ModSlot slot)
    => slot.EquippedMod.Match(
      Some: mod => $"{slot.SlotName}: {mod.Name}",
      None: () => $"{slot.SlotName}: — empty —");

  private void RebuildBrowser()
  {
    GetNode<Button>("%UnequipButton").Visible = false;
    ClearChildren(GetNode<VBoxContainer>("%ArmoryList"));
  }

  private void AddLabelRow(Container parent, string text)
  {
    parent.AddChild(new RichTextLabel { Text = text, FitContent = true, ScrollActive = false });
  }

  private void AddSlotButton(Container parent, string text)
  {
    parent.AddChild(new Button { Text = text });
  }

  private static void ClearChildren(Container parent)
  {
    foreach (Node child in parent.GetChildren())
    {
      parent.RemoveChild(child);
      child.QueueFree();
    }
  }
}
