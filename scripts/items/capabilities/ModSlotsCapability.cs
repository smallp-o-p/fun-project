using FunProject.Stats;
using FunProject.Battle;
using Godot.Collections;
using System;

namespace FunProject.Items.Capabilities;

public class ModSlotsCapability : ItemCapability
{
  private readonly Array<ModSlot> _slots = [];
  public Array<ModSlot> Slots
  {
    get
    {
      _battleAccess.RequireRead(); _battleAccess.Guard?.RequireHostThread();
      return _battleAccess.Guard is null ? _slots : new Array<ModSlot>(_slots);
    }
  }
  internal override void BindBattleAccess(BattleAccessGuard access)
  {
    base.BindBattleAccess(access);
    foreach (var slot in _slots) slot.BindBattleAccess(access);
  }
  internal override void UnbindBattleAccess(BattleAccessGuard access)
  {
    foreach (var slot in _slots) slot.UnbindBattleAccess(access);
    base.UnbindBattleAccess(access);
  }

  public ModSlotsCapability(ModSlotsCapabilityData data)
  {
    for (int i = 0; i < Math.Max(0, data.SlotCount); i++)
    {
      _slots.Add(new ModSlot());
    }
  }
}
