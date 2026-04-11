using Godot;

namespace FunProject.Stats;

#nullable enable
[GlobalClass]
public partial class ModSlot : Resource
{
  [Export] public string SlotName { get; set; } = "Mod Slot";

  public EquippableMod? EquippedMod { get; set; }

  public bool HasMod => EquippedMod != null;

  public void Equip(EquippableMod mod) => EquippedMod = mod;

  public EquippableMod? Unequip()
  {
    var m = EquippedMod;
    EquippedMod = null;
    return m;
  }
}

public interface HasModSlots
{
  Godot.Collections.Array<ModSlot> GetModSlots();
}
