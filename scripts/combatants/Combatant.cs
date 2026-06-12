using System;
using System.Collections.Generic;
using FunProject.Stats;

namespace FunProject.Combatants;

public class Combatant : HasStats, HasModSlots
{
  public string Name { get; }
  public Faction OwningFaction { get; }
  private readonly StatSheet _stats;
  private readonly Godot.Collections.Array<ModSlot> _modSlots = [];

  public Combatant(CombatantData data, Faction faction)
  {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(faction);

    Name = data.Name;
    OwningFaction = faction;
    _stats = new StatSheet(new Dictionary<Type, Stat>
    {
      [typeof(HealthStat)] = data.HealthStat,
      [typeof(ActionPointsStat)] = data.ActionPointsStat,
      [typeof(WillStat)] = data.WillStat,
      [typeof(MovementStat)] = data.MovementStat,
      [typeof(VisionStat)] = data.VisionStat,
      [typeof(AimStat)] = data.AimStat,
    });

    for (int i = 0; i < data.ModSlotCount; i++)
    {
      _modSlots.Add(new ModSlot());
    }
  }

  public Option<Stat> TryGetStat(Type statType) => _stats.TryGetStat(statType);

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat => _stats.TryGetStat<TStat>();

  public TStat GetStat<TStat>() where TStat : Stat => _stats.GetStat<TStat>();

  public Godot.Collections.Array<ModSlot> GetModSlots() => _modSlots;
}
