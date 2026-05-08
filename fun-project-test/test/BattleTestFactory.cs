using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Stats;
using Godot;
using System.Collections.Generic;

internal static class BattleTestFactory
{
  public static Combatant MakeCombatant(
    string name,
    Faction faction,
    int health = 20,
    int actionPoints = 4,
    int movement = 12,
    int vision = 20)
  {
    return new Combatant(new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = health },
      ActionPointsStat = new ActionPointsStat { BaseValue = actionPoints },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = movement },
      VisionStat = new VisionStat { BaseValue = vision },
      AimStat = new AimStat { BaseValue = 65 },
      BaseArmorStat = new BaseArmorStat { BaseValue = 0 },
      ModSlotCount = 0,
    }, faction);
  }

  public static Faction MakeFaction(string name)
  {
    return new Faction(new FactionData
    {
      Name = name,
      Description = $"{name} faction"
    });
  }

  public static Grenade MakeGrenade(string name, int throwRange = 4, int actionPointCost = 1)
  {
    return new Grenade(new GrenadeData
    {
      Name = name,
      Description = $"{name} grenade",
      ThrowRange = throwRange,
      ActionPointCost = actionPointCost,
      MaxCharges = 1,
      ConsumesOnUse = true,
      BlastRadius = 1,
    });
  }

  public static BattleSession MakeSession(
    Vector3I dimensions,
    IEnumerable<Faction> globalFactionOrder)
  {
    return MakeSession(
      dimensions,
      globalFactionOrder,
      new Dictionary<Faction, IEnumerable<Combatant>>());
  }

  public static BattleSession MakeSession(
    Vector3I dimensions,
    IEnumerable<Faction> globalFactionOrder,
    IDictionary<Faction, IEnumerable<Combatant>> factionRosters)
  {
    return new BattleSession(
      new BattleBoardState(dimensions),
      globalFactionOrder,
      factionRosters);
  }
}
