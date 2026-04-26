using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class ModSlot : Resource
{
  [Export] public string SlotName { get; set; } = "Mod Slot";

  public Option<EquippableMod> EquippedMod { get; set; }

  public bool HasMod => EquippedMod.IsSome;

  public void Equip(EquippableMod mod) => EquippedMod = Some(mod);

  public Option<EquippableMod> Unequip()
  {
    var m = EquippedMod;
    EquippedMod = None;
    return m;
  }
}

public interface HasModSlots
{
  Godot.Collections.Array<ModSlot> GetModSlots();
}
