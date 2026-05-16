using System;
using System.Collections.Generic;
using FunProject.Stats;
using Godot;

namespace FunProject.Combatants;

public class Combatant : HasStats, HasModSlots
{
  public string Name { get; }
  public Faction OwningFaction { get; }
  private readonly Dictionary<Type, Stat> _stats;
  private readonly Godot.Collections.Array<ModSlot> _modSlots = [];

  public Combatant(CombatantData data, Faction faction)
  {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(faction);

    Name = data.Name;
    OwningFaction = faction;
    _stats = new Dictionary<Type, Stat>
    {
      [typeof(HealthStat)] = data.HealthStat,
      [typeof(ActionPointsStat)] = data.ActionPointsStat,
      [typeof(WillStat)] = data.WillStat,
      [typeof(MovementStat)] = data.MovementStat,
      [typeof(VisionStat)] = data.VisionStat,
      [typeof(AimStat)] = data.AimStat,
      [typeof(BaseArmorStat)] = data.BaseArmorStat,
    };

    for (int i = 0; i < data.ModSlotCount; i++)
    {
      _modSlots.Add(new ModSlot());
    }
  }

  public Option<Stat> TryGetStat(Type statType)
  {
    return _stats.TryGetValue(statType, out var stat)
      ? Some(stat)
      : None;
  }

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat
  {
    if (_stats.TryGetValue(typeof(TStat), out var foundStat))
    {
      return Some((TStat)foundStat);
    }

    return None;
  }

  public TStat GetStat<TStat>() where TStat : Stat
  {
    return TryGetStat<TStat>().Match(
      stat => stat,
      () => throw new InvalidOperationException());
  }

  public Godot.Collections.Array<ModSlot> GetModSlots() => _modSlots;
}
