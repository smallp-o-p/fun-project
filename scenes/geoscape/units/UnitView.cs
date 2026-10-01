using CampaignGameState = FunProject.GameState.GameState;
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

/// <summary>
/// Present a summary of a Combatant. Stats, equipment slots and armory are accessible from this view.
/// </summary>
public sealed partial class UnitView : GeoscapeView
{
  [Export] public PackedScene SkillProgressionViewScene { get; set; } = null!;

  private CampaignGameState? _state;
  private Combatant? _unit;
  private UnitViewSlot? _selection = UnitViewSlot.Weapon;
  private readonly SysColGeneric.Dictionary<UnitViewSlot, Button> _slotButtons = [];

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%UnequipButton").Pressed += OnUnequipPressed;
    GetNode<Button>("%WeaponSlot").Pressed += () => SelectSlot(UnitViewSlot.Weapon);
    GetNode<Button>("%ArmorSlot").Pressed += () => SelectSlot(UnitViewSlot.Armor);
    GetNode<Button>("%PathsButton").Pressed += OnPathsPressed;
  }

  public void BindUnit(Combatant unit) => _unit = unit;

  public void Present(CampaignGameState state, Combatant unit)
  {
    _state = state;
    _unit = unit;
    _selection = UnitViewSlot.Weapon;
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

    var paths = SkillProgressionViewScene.InstantiateAs<SkillProgressionView>();
    paths.Unit = _unit;
    RequestView(paths);
  }

  /// <summary>Test seam mirroring a slot-button click; production clicks call this too.</summary>
  public void SelectSlot(UnitViewSlot slot)
  {
    _selection = slot;
    RefreshSlotSelection();
    RebuildBrowser();
  }

  private void RebuildAll()
  {
    if (_state is null || _unit is null)
      return;

    GetNode<RichTextLabel>("%Title").Text = _unit.Name;
    GetNode<Label>("%RankLabel").Text = _unit.Rank.RankName;
    RebuildStats();
    RebuildEquipment();
    RebuildBrowser();
  }

  private void RebuildStats()
  {
    var stats = GetNode<Container>("%StatsList");
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
    AddLabelRow(GetNode<Container>("%StatsList"), $"{label}: {baseValue} -> {effective}");
  }

  private void RebuildEquipment()
  {
    _slotButtons.Clear();
    _slotButtons[UnitViewSlot.Weapon] = GetNode<Button>("%WeaponSlot");
    _slotButtons[UnitViewSlot.Armor] = GetNode<Button>("%ArmorSlot");
    RebuildPersonalMods();
    RebuildWeaponSlot();
    RebuildArmorSlot();
    RebuildUtilitySlots();
    RefreshSlotSelection();
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
    GetNode<Button>("%WeaponSlot").TooltipText = "";
    GetNode<Button>("%WeaponSlot").Text = _unit!.EquippedWeapon.Match(
      Some: weapon =>
      {
        Godot.Collections.Array<ModSlot> modSlots = weapon.GetModSlots();
        for (int i = 0; i < modSlots.Count; i++)
          AddSlotButton(weaponMods, SlotLabel(modSlots[i]), UnitViewSlot.WeaponMod(i));
        GetNode<Button>("%WeaponSlot").TooltipText = WeaponSummary(weapon);
        return $"Weapon: {weapon.ItemName}";
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
    RebuildSelectedItem();

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
        var button = BrowserButton($"{line.Mod.Name}  {StockText(line.Unlimited, line.Remaining)}", line.Mod.Description);
        button.Pressed += () => EquipMod(line);
        list.AddChild(button);
      }
      RefreshEmptyArmory(list);
      return;
    }

    foreach (ArmoryItemStock line in _state.Armory.ItemStock())
    {
      // Available folds the exhaustion and inactive-unlimited cases; registration alone
      // does not list an item.
      if (!line.Available || !AcceptsItem(slot, line.Data))
        continue;
      var row = BrowserButton($"{line.Data.Name}  {StockText(line.Unlimited, line.Remaining)}", line.Data.Description);
      row.Pressed += () => EquipItem(line);
      list.AddChild(row);
    }
    RefreshEmptyArmory(list);
  }

  private void RefreshSlotSelection()
  {
    foreach ((UnitViewSlot slot, Button button) in _slotButtons)
      button.SetPressedNoSignal(_selection == slot);
  }

  private static Button BrowserButton(string text, string description) => new()
  {
    Text = text,
    TooltipText = string.IsNullOrWhiteSpace(description) ? text : $"{text}\n{description}",
    ClipText = true,
    Alignment = HorizontalAlignment.Left,
    CustomMinimumSize = new Vector2(0, 36),
  };

  private void RefreshEmptyArmory(Container list)
    => GetNode<Label>("%ArmoryEmptyLabel").Visible = list.GetChildCount() == 0;

  private void RebuildSelectedItem()
  {
    if (_selection is not UnitViewSlot slot || _unit is null)
      return;

    GetNode<Label>("%SelectedSlotLabel").Text = slot.Kind switch
    {
      UnitViewSlotKind.Weapon => "Weapon",
      UnitViewSlotKind.Armor => "Armor",
      UnitViewSlotKind.Utility => $"Utility {slot.Index + 1}",
      UnitViewSlotKind.PersonalMod => $"Personal mod {slot.Index + 1}",
      _ => $"Weapon mod {slot.Index + 1}",
    };
    string name = "Empty slot";
    string details = "Choose an available item below.";
    void ShowItem(EquippableItem item)
    {
      name = item.ItemName;
      details = item.ItemDescription;
      if (item is Weapon weapon)
        details = $"{WeaponSummary(weapon)}\n{details}";
      item.FindCapability<ArmorCapability>().IfSome(armor => details = $"ARMOR {armor.Max}\n{details}");
      if (string.IsNullOrWhiteSpace(details))
        details = "Equipped";
    }
    void ShowMod(ModSlot modSlot)
      => modSlot.EquippedMod.IfSome(mod =>
      {
        name = mod.Name;
        details = string.IsNullOrWhiteSpace(mod.Description) ? "Equipped mod" : mod.Description;
      });

    switch (slot.Kind)
    {
      case UnitViewSlotKind.Weapon:
        _unit.EquippedWeapon.IfSome(weapon => ShowItem(weapon));
        break;
      case UnitViewSlotKind.Armor:
        _unit.EquippedArmor.IfSome(armor => ShowItem(armor.Item));
        break;
      case UnitViewSlotKind.Utility:
        if (_unit.Inventory.TryGetValue(slot.Index, out EquippableItem? item))
          ShowItem(item);
        break;
      case UnitViewSlotKind.PersonalMod:
        ShowMod(_unit.GetModSlots()[slot.Index]);
        break;
      case UnitViewSlotKind.WeaponMod:
        WeaponModSlot(slot.Index).IfSome(ShowMod);
        break;
    }
    GetNode<Label>("%SelectedItemName").Text = name;
    GetNode<RichTextLabel>("%SelectedItemDetails").Text = details.Trim();
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
    var label = new RichTextLabel
    {
      Text = text,
      FitContent = true,
      ScrollActive = false,
      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
      AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    label.AddThemeFontSizeOverride("normal_font_size", 14);
    parent.AddChild(label);
  }

  private void AddSlotButton(Container parent, string text, UnitViewSlot slot)
  {
    var button = new Button
    {
      Text = text,
      TooltipText = text,
      ToggleMode = true,
      ClipText = true,
      Alignment = HorizontalAlignment.Left,
      CustomMinimumSize = new Vector2(0, 32),
    };
    _slotButtons[slot] = button;
    button.Pressed += () => SelectSlot(slot);
    parent.AddChild(button);
  }
}
