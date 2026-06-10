using FunProject.Stats;
using Godot.Collections;
using System;

namespace FunProject.Items.Capabilities;

public class ModSlotsCapability : ItemCapability
{
  public Array<ModSlot> Slots { get; } = [];

  public ModSlotsCapability(ModSlotsCapabilityData data)
  {
    for (int i = 0; i < Math.Max(0, data.SlotCount); i++)
    {
      Slots.Add(new ModSlot());
    }
  }
}
