using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Core;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace FunProject.Items;

public class EquippableItem : HasModSlots, HasNameAndDescription
{
  private protected readonly BattleAccessBinding _battleAccess = new(shared: true);
  internal virtual void BindBattleAccess(BattleAccessGuard access)
  {
    _battleAccess.Bind(access);
    foreach (var capability in _capabilities) capability.BindBattleAccess(access);
  }
  internal virtual void UnbindBattleAccess(BattleAccessGuard access)
  {
    foreach (var capability in _capabilities) capability.UnbindBattleAccess(access);
    _battleAccess.Unbind(access);
  }

  private readonly System.Collections.Generic.List<ItemCapability> _capabilities = [];

  public string ItemName { get; }
  public string ItemDescription { get; }
  public EquippableItemData Data { get; }

  public EquippableItem(EquippableItemData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    Data = data;
    ItemName = data.Name;
    ItemDescription = data.Description;

    foreach (ItemCapabilityData capabilityData in data.Capabilities)
    {
      ItemCapability capability = capabilityData.CreateRuntime();
      if (_capabilities.AsValueEnumerable().Any(existing => existing.GetType().IsAssignableTo(capability.GetType())
                                     || capability.GetType().IsAssignableTo(existing.GetType())))
        throw new InvalidOperationException($"Item '{ItemName}' has more than one {capability.GetType().Name}.");
      _capabilities.Add(capability);
    }
  }

  public string GetName() => ItemName;
  public string GetDescription() => ItemDescription;

  public Option<TCap> FindCapability<TCap>() where TCap : ItemCapability
  {
    foreach (var capability in _capabilities.AsValueEnumerable().OfType<TCap>())
      return capability;
    return None;
  }

  public Option<ItemWith<TCap>> With<TCap>() where TCap : ItemCapability
    => FindCapability<TCap>().Map(capability => new ItemWith<TCap>(this, capability));

  public Array<ModSlot> GetModSlots()
  {
    _battleAccess.RequireRead(); _battleAccess.Guard?.RequireHostThread(); return FindCapability<ModSlotsCapability>().Match(
      capability => capability.Slots,
      () => new Array<ModSlot>());
  }

  public IReadOnlyList<Buff> GrantedBuffs
    => FindCapability<BuffGrantCapability>().Match(
        capability => capability.Buffs,
        () => (IReadOnlyList<Buff>)[]);
}
