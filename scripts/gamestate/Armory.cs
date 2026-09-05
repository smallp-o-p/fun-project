using FunProject.Items;
using FunProject.Stats;
using System;
using System.Collections.Generic;

namespace FunProject.GameState;

/// <summary>One item shelf line for the UI: the mold, the stock policy, the stock count left,
/// and whether anything can actually be withdrawn right now.</summary>
public sealed record ArmoryItemStock(EquippableItemData Data, bool Unlimited, int Remaining)
{
  public bool Available => Unlimited || Remaining > 0;
}

/// <summary>One mod shelf line for the UI: the template, the stock policy, and the count left.</summary>
public sealed record ArmoryModStock(EquippableMod Mod, bool Unlimited, int Remaining);

/// <summary>
/// Campaign armory: the player's stock of equippable items and mods — anonymous, count-based,
/// with factory-fresh materialization on withdrawal. Stock POLICY is authored (the
/// <c>UnlimitedStock</c> flag on the item/mod template); stock QUANTITIES are runtime-only.
/// Campaign start seeds a shelf for each initially supplied item; manufacturing creates
/// other shelves on first completion. An unlimited shelf always permits withdrawal;
/// scarce shelves retain their counts, including zero. <see cref="AddStock"/> and
/// <see cref="AddModStock"/> grow counted stock. Items are materialized fresh from their
/// template on withdraw, so per-instance state (attached mods, charges, magazine) does not
/// survive the shelf: depositing an item returns its equipped mods to the mod shelf and
/// counts the item back into stock. Authored resources are unique instances; shelves key on
/// reference identity.
/// </summary>
public sealed class Armory
{
  private sealed class ItemShelf
  {
    public required EquippableItemData Data { get; init; }
    public required bool Unlimited { get; init; }
    public int Remaining { get; set; }
  }

  private sealed class ModShelf
  {
    public required EquippableMod Mod { get; init; }
    public required bool Unlimited { get; init; }
    public int Remaining { get; set; }
  }

  private readonly Dictionary<EquippableItemData, ItemShelf> _items = [];
  private readonly Dictionary<EquippableMod, ModShelf> _mods = [];

  public Armory(IReadOnlyList<EquippableItemData> items, IReadOnlyList<EquippableMod> mods)
  {
    ArgumentNullException.ThrowIfNull(items);
    ArgumentNullException.ThrowIfNull(mods);

    foreach (EquippableItemData item in items)
    {
      ArgumentNullException.ThrowIfNull(item);
      var shelf = new ItemShelf
      {
        Data = item,
        Unlimited = item.UnlimitedStock,
        Remaining = 1,
      };
      if (!_items.TryAdd(item, shelf))
        throw new InvalidOperationException($"Armory lists item '{item.Name}' more than once.");
    }

    foreach (EquippableMod mod in mods)
    {
      ArgumentNullException.ThrowIfNull(mod);
      var shelf = new ModShelf { Mod = mod, Unlimited = mod.UnlimitedStock, Remaining = 1 };
      if (!_mods.TryAdd(mod, shelf))
        throw new InvalidOperationException($"Mod stock lists '{mod.Name}' more than once.");
    }
  }

  /// <summary>Add one produced item using the campaign's captured stock policy.
  /// Creates missing shelves; existing unlimited supply rejects another delivery.</summary>
  internal void AddItem(EquippableItemData data, bool unlimited)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (_items.ContainsKey(data))
    {
      AddStock(data, 1);
      return;
    }
    _items.Add(data, new ItemShelf { Data = data, Unlimited = unlimited, Remaining = 1 });
  }

  /// <summary>
  /// Add <paramref name="count"/> stock of a listed scarce item.
  /// Unknown or unlimited entries throw.
  /// </summary>
  public void AddStock(EquippableItemData data, int count)
  {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentOutOfRangeException.ThrowIfLessThan(count, 0);
    if (!_items.TryGetValue(data, out ItemShelf? shelf))
      throw new InvalidOperationException($"Armory has no entry for item '{data.Name}'.");
    if (shelf.Unlimited)
      throw new InvalidOperationException($"Armory item '{data.Name}' is unlimited; its stock is never counted.");

    shelf.Remaining = checked(shelf.Remaining + count);
  }

  /// <summary>
  /// Add <paramref name="count"/> stock of a listed scarce mod — the mod-shelf mirror of
  /// <see cref="AddStock"/>. Unknown or unlimited entries throw.
  /// </summary>
  public void AddModStock(EquippableMod mod, int count)
  {
    ArgumentNullException.ThrowIfNull(mod);
    ArgumentOutOfRangeException.ThrowIfLessThan(count, 0);
    if (!_mods.TryGetValue(mod, out ModShelf? shelf))
      throw new InvalidOperationException($"Armory has no mod-stock entry for '{mod.Name}'.");
    if (shelf.Unlimited)
      throw new InvalidOperationException($"Armory mod '{mod.Name}' is unlimited; its stock is never counted.");

    shelf.Remaining = checked(shelf.Remaining + count);
  }

  /// <summary>Materialize one factory-fresh instance of this item, or None when its shelf
  /// is missing or its scarce stock is exhausted.</summary>
  public Option<EquippableItem> TryWithdrawItem(EquippableItemData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (!_items.TryGetValue(data, out ItemShelf? shelf) || shelf is { Unlimited: false, Remaining: 0 })
      return None;

    // Construct before consuming: a malformed template throws from the factory and the
    // scarce count must survive it.
    EquippableItem item = ItemRuntimeFactory.Create(data);
    if (!shelf.Unlimited)
      shelf.Remaining--;
    return item;
  }

  /// <summary>
  /// Return an item to stock: its equipped mods go back to the mod shelf, the template
  /// counts back in. Items without a shelf throw. Per-instance state (mods, charges,
  /// magazine) is deliberately not preserved — stock is anonymous.
  /// </summary>
  public void DepositItem(EquippableItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (!_items.TryGetValue(item.Data, out ItemShelf? shelf))
      throw new InvalidOperationException(
        $"Armory has no entry for item '{item.ItemName}'; anything withdrawable must be authored.");

    foreach (ModSlot slot in item.GetModSlots())
      slot.Unequip().IfSome(DepositMod);
    if (!shelf.Unlimited)
      shelf.Remaining = checked(shelf.Remaining + 1);
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
      shelf.Remaining = checked(shelf.Remaining + 1);
  }

  public Option<ArmoryItemStock> TryGetItemStock(EquippableItemData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    return _items.TryGetValue(data, out ItemShelf? shelf) ? StockRow(shelf) : None;
  }

  public bool HasAvailableItem(EquippableItemData data)
    => TryGetItemStock(data).Match(line => line.Available, () => false);

  public IReadOnlyList<ArmoryItemStock> ItemStock()
  {
    List<ArmoryItemStock> stock = [];
    foreach (ItemShelf shelf in _items.Values)
      stock.Add(StockRow(shelf));
    return stock;
  }

  private static ArmoryItemStock StockRow(ItemShelf shelf) => new(
    shelf.Data,
    shelf.Unlimited,
    shelf.Remaining);

  public IReadOnlyList<ArmoryModStock> ModStock()
  {
    List<ArmoryModStock> stock = [];
    foreach (ModShelf shelf in _mods.Values)
      stock.Add(new ArmoryModStock(shelf.Mod, shelf.Unlimited, shelf.Remaining));
    return stock;
  }
}
