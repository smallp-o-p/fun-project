using FunProject.Core;
using FunProject.Stats;
using Godot.Collections;
using System;

namespace FunProject.Items;

public class EquippableItem : HasModSlots, HasNameAndDescription
{
  protected readonly Array<ModSlot> ModSlots = [];

  public string ItemName { get; }
  public string ItemDescription { get; }
  public int MaxCharges { get; }
  public int CurrentCharges { get; private set; }
  public bool IsDepleted => CurrentCharges <= 0;

  public EquippableItem(EquippableItemData data)
  {
    ItemName = data.Name;
    ItemDescription = data.Description;
    MaxCharges = Math.Max(0, data.MaxCharges);
    CurrentCharges = MaxCharges;

    for (int i = 0; i < data.ModSlotCount; i++)
    {
      ModSlots.Add(new ModSlot());
    }
  }

  public Array<ModSlot> GetModSlots() => ModSlots;
  public string GetName() => ItemName;
  public string GetDescription() => ItemDescription;

  public bool TrySpendCharge(int amount = 1)
  {
    if (amount < 0 || CurrentCharges < amount)
      return false;

    CurrentCharges -= amount;
    return true;
  }

  public void RestoreCharges()
  {
    CurrentCharges = MaxCharges;
  }
}
