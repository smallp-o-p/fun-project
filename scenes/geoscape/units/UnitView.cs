using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Strategic;
using FunProject.Weapons;
using Godot;
using System;
using FunProject.Scenes.Ext;

// Which slot the armory browser targets. Produced by slot-button clicks (or the test seam);
// weapon/armor/utility/mod kinds each filter the browser differently.
public enum UnitViewSlotKind
{
  Weapon,
  Armor,
  Utility,
  PersonalMod,
  WeaponMod,
}

public readonly record struct UnitViewSlot(UnitViewSlotKind Kind, int Index = 0)
{
  public static UnitViewSlot Weapon => new(UnitViewSlotKind.Weapon);
  public static UnitViewSlot Armor => new(UnitViewSlotKind.Armor);
  public static UnitViewSlot Utility(int index) => new(UnitViewSlotKind.Utility, index);
  public static UnitViewSlot PersonalMod(int index) => new(UnitViewSlotKind.PersonalMod, index);
  public static UnitViewSlot WeaponMod(int index) => new(UnitViewSlotKind.WeaponMod, index);
}

// Full-screen soldier screen: stats page (base -> effective), equipment slots, and the
// campaign armory browser. Presentation only — every interaction calls domain operations
// on GameState/Combatant/Armory and re-presents; nothing is cached beyond the bound unit.
// Activation (Present(state, session)) refreshes the retained unit WITHOUT clearing the
// equipment selection, so returning from skill paths keeps the inspected slot. The Paths
// button builds a fresh SkillProgressionView, binds the unit before it enters the tree,
// and requests it.
public sealed partial class UnitView : GeoscapeView
{
  [Export] public PackedScene? SkillProgressionViewScene { get; set; }

  private CampaignGameState? _state;
  private Combatant? _unit;
  private UnitViewSlot? _selection;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%UnequipButton").Pressed += OnUnequipPressed;
    GetNode<Button>("%WeaponSlot").Pressed += () => SelectSlot(UnitViewSlot.Weapon);
    GetNode<Button>("%ArmorSlot").Pressed += () => SelectSlot(UnitViewSlot.Armor);
    GetNode<Button>("%PathsButton").Pressed += OnPathsPressed;
  }

  // The roster binds the selected combatant before requesting this view.
  public void BindUnit(Combatant unit) => _unit = unit;

  // Direct-caller entry point (tests, future callers): full rebuild with a fresh unit.
  public void Present(CampaignGameState state, Combatant unit)
  {
    _state = state;
    _unit = unit;
    _selection = null;
    RebuildAll();
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    if (_unit is null)
      throw new InvalidOperationException(
        "UnitView requires a bound combatant; the roster binds it before requesting the view.");
    _state = state;
    RebuildAll(); // keeps _selection: returning from paths refreshes without clearing it
  }

  private void OnPathsPressed()
  {
    if (_unit is null)
      throw new InvalidOperationException(
        "UnitView requires a bound combatant before opening skill paths.");
    PackedScene scene = SkillProgressionViewScene ?? throw new InvalidOperationException(
      "UnitView requires SkillProgressionViewScene; assign a PackedScene in the inspector.");
    Node instance = scene.Instantiate();
    if (instance is not SkillProgressionView paths)
    {
      instance.Free(); // free now: rejected roots must not linger to frame end
      throw new InvalidOperationException(
        "UnitView requires SkillProgressionViewScene whose root is a SkillProgressionView.");
    }
    paths.BindUnit(_unit); // retain the unit before the view enters the tree
    RequestView(paths);
  }

  /// <summary>Test seam mirroring a slot-button click; production clicks call this too.</summary>
  public void SelectSlot(UnitViewSlot slot)
  {
    _selection = slot;
    RebuildBrowser();
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
    stats.QueueFreeAllChildren();
    var contributions = _state!.CampaignStatContributions(_unit!);
    AddStatRow<HealthStat>("Health", contributions);
    AddStatRow<ActionPointsStat>("Action Points", contributions);
    AddStatRow<WillStat>("Will", contributions);
    AddStatRow<MovementStat>("Movement", contributions);
    AddStatRow<VisionStat>("Vision", contributions);
    AddStatRow<AimStat>("Aim", contributions);

    var buffs = GetNode<VBoxContainer>("%BuffsList");
    buffs.QueueFreeAllChildren();
    foreach (Buff buff in _unit!.InnateBuffs)
      AddLabelRow(buffs, buff.Name);
    if (buffs.GetChildCount() == 0)
      AddLabelRow(buffs, "—");
  }

  private void AddStatRow<TStat>(string label, SysColGeneric.IEnumerable<StatMod> contributions)
    where TStat : Stat
  {
    int baseValue = Mathf.RoundToInt(_unit!.GetStat<TStat>().BaseValue);
    int effective = Mathf.RoundToInt(_unit.Resolve<TStat>(contributions));
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
    slots.QueueFreeAllChildren();
    Godot.Collections.Array<ModSlot> modSlots = _unit!.GetModSlots();
    for (int i = 0; i < modSlots.Count; i++)
      AddSlotButton(slots, SlotLabel(modSlots[i]), UnitViewSlot.PersonalMod(i));
  }

  private void RebuildWeaponSlot()
  {
    var weaponMods = GetNode<VBoxContainer>("%WeaponModSlots");
    weaponMods.QueueFreeAllChildren();
    GetNode<Button>("%WeaponSlot").Text = _unit!.EquippedWeapon.Match(
      Some: weapon =>
      {
        Godot.Collections.Array<ModSlot> modSlots = weapon.GetModSlots();
        for (int i = 0; i < modSlots.Count; i++)
          AddSlotButton(weaponMods, SlotLabel(modSlots[i]), UnitViewSlot.WeaponMod(i));
        return $"Weapon: {WeaponSummary(weapon)}";
      },
      None: () => "Weapon: — empty —");
  }

  private static string WeaponSummary(Weapon weapon)
  {
    int damage = weapon.EmitDamage().AsValueEnumerable().Sum(packet => packet.Amount);
    string ammo = weapon is AmmunitionedWeapon magazine
      ? $"  AMMO {magazine.CurrentAmmo}/{magazine.MagazineSize}"
      : "";
    return $"{weapon.ItemName}  DMG {damage}  RNG {weapon.EffectiveRange}  "
      + $"CRIT {Mathf.RoundToInt(weapon.EffectiveStat<CriticalChanceStat>())}{ammo}";
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
    slots.QueueFreeAllChildren();
    for (int i = 0; i < _unit!.MaxInventorySize; i++)
    {
      string text = _unit.Inventory.TryGetValue(i, out EquippableItem? item)
        ? $"Utility {i + 1}: {item.ItemName}"
        : $"Utility {i + 1}: — empty —";
      AddSlotButton(slots, text, UnitViewSlot.Utility(i));
    }
  }

  private static string SlotLabel(ModSlot slot)
    => slot.EquippedMod.Match(
      Some: mod => $"{slot.SlotName}: {mod.Name}",
      None: () => $"{slot.SlotName}: — empty —");

  private void RebuildBrowser()
  {
    var list = GetNode<VBoxContainer>("%ArmoryList");
    var unequip = GetNode<Button>("%UnequipButton");
    list.QueueFreeAllChildren();

    if (_selection is null || _state is null || _unit is null)
    {
      unequip.Visible = false;
      return;
    }

    unequip.Visible = SelectionIsOccupied();

    UnitViewSlot slot = _selection.Value;
    if (slot.Kind is UnitViewSlotKind.PersonalMod or UnitViewSlotKind.WeaponMod)
    {
      foreach (ArmoryModStock line in _state.Armory.ModStock())
      {
        if (!line.Unlimited && line.Remaining == 0)
          continue;
        var button = new Button { Text = $"{line.Mod.Name}  {StockText(line.Unlimited, line.Remaining)}" };
        button.Pressed += () => EquipMod(line);
        list.AddChild(button);
      }
      return;
    }

    foreach (ArmoryItemStock line in _state.Armory.ItemStock())
    {
      // Available folds the exhaustion and inactive-unlimited cases; registration alone
      // does not list an item.
      if (!line.Available || !AcceptsItem(slot, line.Data))
        continue;
      var row = new Button { Text = $"{line.Data.Name}  {StockText(line.Unlimited, line.Remaining)}" };
      row.Pressed += () => EquipItem(line);
      list.AddChild(row);
    }
  }

  private static string StockText(bool unlimited, int remaining)
    => unlimited ? "∞" : $"x{remaining}";

  private bool SelectionIsOccupied() => _selection!.Value.Kind switch
  {
    UnitViewSlotKind.Weapon => _unit!.EquippedWeapon.IsSome,
    UnitViewSlotKind.Armor => _unit!.EquippedArmor.IsSome,
    UnitViewSlotKind.Utility => _unit!.Inventory.ContainsKey(_selection!.Value.Index),
    UnitViewSlotKind.PersonalMod => _unit!.GetModSlots()[_selection!.Value.Index].HasMod,
    UnitViewSlotKind.WeaponMod => WeaponModSlot(_selection!.Value.Index).Match(Some: s => s.HasMod, None: () => false),
    _ => false,
  };

  private void OnUnequipPressed()
  {
    UnitViewSlot slot = _selection!.Value;
    switch (slot.Kind)
    {
      case UnitViewSlotKind.Weapon:
        _unit!.UnequipWeapon().IfSome(old => _state!.Armory.DepositItem(old));
        break;
      case UnitViewSlotKind.Armor:
        _unit!.UnequipArmor().IfSome(old => _state!.Armory.DepositItem(old.Item));
        break;
      case UnitViewSlotKind.Utility:
        _unit!.UnequipItem(slot.Index).IfSome(old => _state!.Armory.DepositItem(old));
        break;
      case UnitViewSlotKind.PersonalMod:
        _unit!.GetModSlots()[slot.Index].Unequip().IfSome(mod => _state!.Armory.DepositMod(mod));
        break;
      case UnitViewSlotKind.WeaponMod:
        WeaponModSlot(slot.Index).IfSome(modSlot => modSlot.Unequip().IfSome(mod => _state!.Armory.DepositMod(mod)));
        break;
    }
    RebuildAll();
  }

  private static bool AcceptsItem(UnitViewSlot slot, EquippableItemData data) => slot.Kind switch
  {
    UnitViewSlotKind.Weapon => data is WeaponData,
    UnitViewSlotKind.Armor => HasArmorCapability(data),
    UnitViewSlotKind.Utility => true,
    _ => false,
  };

  private static bool HasArmorCapability(EquippableItemData data)
  {
    foreach (ItemCapabilityData capability in data.Capabilities)
      if (capability is ArmorCapabilityData)
        return true;
    return false;
  }

  private void EquipItem(ArmoryItemStock line)
  {
    UnitViewSlot slot = _selection!.Value;
    // AcceptsItem's _ => false arm prevents non-item slots from reaching this withdrawal.
    Option<EquippableItem> withdrawn = _state!.Armory.TryWithdrawItem(line.Data);
    withdrawn.IfSome(item =>
    {
      switch (slot.Kind)
      {
        case UnitViewSlotKind.Weapon:
          _unit!.EquipWeapon((Weapon)item).IfSome(old => _state.Armory.DepositItem(old));
          break;
        case UnitViewSlotKind.Armor:
          // AcceptsItem already guarantees armor capability for this slot; trust the filter
          // like the weapon arm does.
          item.With<ArmorCapability>().IfSome(proof =>
            _unit!.EquipArmor(proof).IfSome(old => _state.Armory.DepositItem(old.Item)));
          break;
        case UnitViewSlotKind.Utility:
          _unit!.UnequipItem(slot.Index).IfSome(old => _state.Armory.DepositItem(old));
          _unit.EquipItem(item, slot.Index);
          break;
      }
    });
    RebuildAll();
  }

  private void EquipMod(ArmoryModStock line)
  {
    UnitViewSlot slot = _selection!.Value;
    if (slot.Kind == UnitViewSlotKind.WeaponMod && WeaponModSlot(slot.Index).IsNone)
      return;

    Option<EquippableMod> withdrawn = _state!.Armory.TryWithdrawMod(line.Mod);
    withdrawn.IfSome(mod =>
    {
      if (slot.Kind == UnitViewSlotKind.PersonalMod)
      {
        ModSlot target = _unit!.GetModSlots()[slot.Index];
        target.Unequip().IfSome(old => _state!.Armory.DepositMod(old));
        target.Equip(mod);
      }
      else
      {
        WeaponModSlot(slot.Index).IfSome(target =>
        {
          target.Unequip().IfSome(old => _state!.Armory.DepositMod(old));
          target.Equip(mod);
        });
      }
    });
    RebuildAll();
  }

  private Option<ModSlot> WeaponModSlot(int index)
    => _unit!.EquippedWeapon.Match(
      Some: weapon => weapon.GetModSlots().Count > index ? weapon.GetModSlots()[index] : Option<ModSlot>.None,
      None: () => Option<ModSlot>.None);

  private void AddLabelRow(Container parent, string text)
  {
    parent.AddChild(new RichTextLabel { Text = text, FitContent = true, ScrollActive = false });
  }

  private void AddSlotButton(Container parent, string text, UnitViewSlot slot)
  {
    var button = new Button { Text = text };
    button.Pressed += () => SelectSlot(slot);
    parent.AddChild(button);
  }
}
