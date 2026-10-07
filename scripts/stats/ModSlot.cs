using FunProject.Battle;
using System.Collections.Generic;
using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class ModSlot : Resource
{
  private readonly BattleAccessBinding _battleAccess = new(shared: true);
  internal void BindBattleAccess(BattleAccessGuard access) => _battleAccess.Bind(access);
  internal void UnbindBattleAccess(BattleAccessGuard access) => _battleAccess.Unbind(access);

  private string _slotName = "Mod Slot";
  [Export]
  public string SlotName
  {
    get { _battleAccess.RequireRead(); return _slotName; }
    set { _battleAccess.RequireMutation(); _slotName = value; }
  }

  private Option<EquippableMod> _equippedMod;
  public Option<EquippableMod> EquippedMod
  {
    get { _battleAccess.RequireRead(); return _equippedMod; }
    set { _battleAccess.RequireMutation(); _equippedMod = value; }
  }

  public bool HasMod => EquippedMod.IsSome;

  public void Equip(EquippableMod mod) { _battleAccess.RequireMutation(); EquippedMod = Some(mod); }

  public Option<EquippableMod> Unequip()
  {
    _battleAccess.RequireMutation();
    var m = EquippedMod;
    EquippedMod = None;
    return m;
  }
}

public interface HasModSlots
{
  Godot.Collections.Array<ModSlot> GetModSlots();
}

public static class HasModSlotsExtensions
{
  public static IEnumerable<EquippableMod> EquippedMods(this HasModSlots host)
    => host.GetModSlots().AsValueEnumerable().Select(slot => slot.EquippedMod).ToArray().Somes();
}
