using FunProject.Stats;
using Godot;
using Godot.Collections;

namespace FunProject.Combatants;

public class Combatant : HasStats, HasModSlots
{
  public string Name { get; }
  public Faction OwningFaction { get; }
  private readonly Dictionary<StatType, Stat> _stats;
  private readonly Array<ModSlot> _modSlots = [];

  public Combatant(CombatantData data, Faction faction)
  {
    Name = data.Name;
    OwningFaction = faction;
    _stats = new Dictionary<StatType, Stat>
    {
      [StatType.Health] = data.HealthStat,
      [StatType.ActionPoints] = data.ActionPointsStat,
      [StatType.Will] = data.WillStat,
      [StatType.Movement] = data.MovementStat,
    };

    for (int i = 0; i < data.ModSlotCount; i++)
    {
      _modSlots.Add(new ModSlot());
    }
  }

  public Dictionary<StatType, Stat> GetStats() => _stats;
  public Array<ModSlot> GetModSlots() => _modSlots;
}
