using FunProject.Items;
using FunProject.Stats;
using System;
using System.Collections.Generic;

namespace FunProject.GameState;

/// <summary>One item shelf line for the UI: the mold, the stock policy, and the instances still on the shelf.</summary>
public sealed record ArmoryItemStock(EquippableItemData Data, bool Unlimited, int Remaining);

/// <summary>One mod shelf line for the UI: the template, the stock policy, and the count left.</summary>
public sealed record ArmoryModStock(EquippableMod Mod, bool Unlimited, int Remaining);

/// <summary>
/// Campaign armory: the player's stock of equippable items and mods. Limited entries hold
/// actual runtime instances (a modded weapon returned to the armory keeps its mods);
/// unlimited entries instantiate fresh on withdraw and discard on deposit. Mods are
/// immutable authored resources (no runtime wrapper), so their shelf is counts only.
/// Authored resources are unique instances; shelves key on reference identity.
/// </summary>
public sealed class Armory
{
  private sealed class ItemShelf
  {
    public required EquippableItemData Data { get; init; }
    public required bool Unlimited { get; init; }
    public List<EquippableItem> Stack { get; } = [];
  }

  private sealed class ModShelf
  {
    public required EquippableMod Mod { get; init; }
    public required bool Unlimited { get; init; }
    public int Remaining { get; set; }
  }

  private readonly Dictionary<EquippableItemData, ItemShelf> _items = [];
  private readonly Dictionary<EquippableMod, ModShelf> _mods = [];

  public Armory(IReadOnlyList<ArmoryEntryData> items, IReadOnlyList<ModStockEntryData> mods)
  {
    ArgumentNullException.ThrowIfNull(items);
    ArgumentNullException.ThrowIfNull(mods);

    foreach (ArmoryEntryData entry in items)
    {
      ArgumentNullException.ThrowIfNull(entry);
      if (entry.Item is null)
        throw new InvalidOperationException("Armory entry has no Item assigned.");
      if (entry.Count < -1)
        throw new InvalidOperationException(
          $"Armory entry for '{entry.Item.Name}' has Count {entry.Count}; -1 means unlimited, otherwise a non-negative count is required.");

      var shelf = new ItemShelf { Data = entry.Item, Unlimited = entry.Count == -1 };
      if (!_items.TryAdd(entry.Item, shelf))
        throw new InvalidOperationException($"Armory lists item '{entry.Item.Name}' more than once.");
      for (int i = 0; i < Math.Max(0, entry.Count); i++)
        shelf.Stack.Add(ItemRuntimeFactory.Create(entry.Item));
    }

    foreach (ModStockEntryData entry in mods)
    {
      ArgumentNullException.ThrowIfNull(entry);
      if (entry.Mod is null)
        throw new InvalidOperationException("Mod-stock entry has no Mod assigned.");
      if (entry.Count < -1)
        throw new InvalidOperationException(
          $"Mod-stock entry for '{entry.Mod.Name}' has Count {entry.Count}; -1 means unlimited, otherwise a non-negative count is required.");

      var shelf = new ModShelf { Mod = entry.Mod, Unlimited = entry.Count == -1, Remaining = Math.Max(0, entry.Count) };
      if (!_mods.TryAdd(entry.Mod, shelf))
        throw new InvalidOperationException($"Mod stock lists '{entry.Mod.Name}' more than once.");
    }
  }

  /// <summary>Take one instance of this item off the shelf, or None when the shelf is empty/unknown.</summary>
  public Option<EquippableItem> TryWithdrawItem(EquippableItemData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (!_items.TryGetValue(data, out ItemShelf? shelf))
      return None;
    if (shelf.Unlimited)
      return ItemRuntimeFactory.Create(data);
    if (shelf.Stack.Count == 0)
      return None;

    EquippableItem item = shelf.Stack[^1];
    shelf.Stack.RemoveAt(shelf.Stack.Count - 1);
    return item;
  }

  /// <summary>Return an item to its shelf. Unlimited shelves discard it; unknown items throw.</summary>
  public void DepositItem(EquippableItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (!_items.TryGetValue(item.Data, out ItemShelf? shelf))
      throw new InvalidOperationException(
        $"Armory has no entry for item '{item.ItemName}'; anything withdrawable must be authored.");
    if (shelf.Unlimited)
    {
      foreach (ModSlot slot in item.GetModSlots())
        slot.Unequip().IfSome(DepositMod);
      return;
    }

    shelf.Stack.Add(item);
  }

  /// <summary>Take one count of this mod template, or None when exhausted/unknown.</summary>
  public Option<EquippableMod> TryWithdrawMod(EquippableMod mod)
  {
    ArgumentNullException.ThrowIfNull(mod);
    if (!_mods.TryGetValue(mod, out ModShelf? shelf))
      return None;
    if (!shelf.Unlimited)
    {
      if (shelf.Remaining == 0)
        return None;
      shelf.Remaining--;
    }
    return mod;
  }

  /// <summary>Return a mod count to its shelf. Unknown mods throw.</summary>
  public void DepositMod(EquippableMod mod)
  {
    ArgumentNullException.ThrowIfNull(mod);
    if (!_mods.TryGetValue(mod, out ModShelf? shelf))
      throw new InvalidOperationException($"Armory has no mod-stock entry for '{mod.Name}'.");
    if (!shelf.Unlimited)
      shelf.Remaining++;
  }

  public IReadOnlyList<ArmoryItemStock> ItemStock()
  {
    List<ArmoryItemStock> stock = [];
    foreach (ItemShelf shelf in _items.Values)
      stock.Add(new ArmoryItemStock(shelf.Data, shelf.Unlimited, shelf.Stack.Count));
    return stock;
  }

  public IReadOnlyList<ArmoryModStock> ModStock()
  {
    List<ArmoryModStock> stock = [];
    foreach (ModShelf shelf in _mods.Values)
      stock.Add(new ArmoryModStock(shelf.Mod, shelf.Unlimited, shelf.Remaining));
    return stock;
  }
}
