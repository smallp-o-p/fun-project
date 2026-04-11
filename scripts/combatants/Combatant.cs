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
    Name = data.Name;
    OwningFaction = faction;
    _stats = new Dictionary<Type, Stat>
    {
      [typeof(HealthStat)] = data.HealthStat,
      [typeof(ActionPointsStat)] = data.ActionPointsStat,
      [typeof(WillStat)] = data.WillStat,
      [typeof(MovementStat)] = data.MovementStat,
      [typeof(AimStat)] = data.AimStat,
      [typeof(BaseArmorStat)] = data.BaseArmorStat,
    };

    for (int i = 0; i < data.ModSlotCount; i++)
    {
      _modSlots.Add(new ModSlot());
    }
  }

  public bool TryGetStat(Type statType, out Stat stat) => _stats.TryGetValue(statType, out stat);

  public bool TryGetStat<TStat>(out TStat stat) where TStat : Stat
  {
    if (_stats.TryGetValue(typeof(TStat), out var foundStat))
    {
      stat = (TStat)foundStat;
      return true;
    }

    stat = null!;
    return false;
  }

  public TStat GetStat<TStat>() where TStat : Stat
  {
    if (TryGetStat<TStat>(out var stat))
      return stat;

    throw new InvalidOperationException();
  }

  public Godot.Collections.Array<ModSlot> GetModSlots() => _modSlots;
}
