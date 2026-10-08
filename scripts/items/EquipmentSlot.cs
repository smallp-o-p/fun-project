using System;

namespace FunProject.Items;

/// <summary>Authored equipment destinations; independent of an item's capabilities.</summary>
[Flags]
public enum EquipmentSlot
{
  None = 0,
  Weapon = 1,
  Armor = 2,
  Utility = 4,
}

public static class EquipmentSlotExtensions
{
  public static bool Supports(this EquipmentSlot compatibleSlots, EquipmentSlot destination)
    => destination is EquipmentSlot.Weapon or EquipmentSlot.Armor or EquipmentSlot.Utility
      && compatibleSlots.HasFlag(destination);
}
