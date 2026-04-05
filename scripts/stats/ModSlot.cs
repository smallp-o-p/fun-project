using Godot;

namespace FunProject.Stats;

#nullable enable
[GlobalClass]
public partial class ModSlot : Resource
{
  [Export] public string SlotName { get; set; } = "Mod Slot";

  public EquippableStatMod? EquippedMod { get; set; }

  public bool HasMod => EquippedMod != null;

  public void Equip(EquippableStatMod mod) => EquippedMod = mod;

  public EquippableStatMod? Unequip()
  {
    var m = EquippedMod;
    EquippedMod = null;
    return m;
  }
}
