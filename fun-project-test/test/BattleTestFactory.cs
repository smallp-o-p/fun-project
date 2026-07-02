using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Tests;
using FunProject.Weapons;
using Godot;
using System.Collections.Generic; // retained for IEnumerable<T>

internal static class BattleTestFactory
{
  public static Combatant MakeCombatant(
    string name,
    Faction faction,
    int health = 20,
    int actionPoints = 4,
    int movement = 12,
    int vision = 20,
    int aim = 65)
  {
    return new Combatant(new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = health },
      ActionPointsStat = new ActionPointsStat { BaseValue = actionPoints },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = movement },
      VisionStat = new VisionStat { BaseValue = vision },
      AimStat = new AimStat { BaseValue = aim },
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

  public static ItemWith<ThrowableCapability> MakeGrenade(string name, int throwRange = 4, int actionPointCost = 1)
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = name,
      Description = $"{name} grenade",
      Capabilities =
      [
        new ThrowableCapabilityData { ThrowRange = throwRange, ActionPointCost = actionPointCost, ConsumesOnUse = true },
        new BlastCapabilityData { BlastRadius = 1 },
        new ChargesCapabilityData { MaxCharges = 1 },
      ],
    });
    return item.With<ThrowableCapability>().RequireSome();
  }

  public static ItemWith<ArmorCapability> MakeArmor(
    string name,
    int armor = 10,
    Element element = Element.Kinetic,
    int regenDelayTurns = 0,
    int regenPerTurn = 0)
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = name,
      Description = $"{name} armor",
      Capabilities =
      [
        new ArmorCapabilityData
        {
          ArmorStat = new BaseArmorStat { BaseValue = armor, Element = element },
          RegenDelayTurns = regenDelayTurns,
          RegenPerTurn = regenPerTurn,
        },
      ],
    });
    return item.With<ArmorCapability>().RequireSome();
  }

  public static WeaponFrameData MakeFrame(params (Element element, float multiplier)[] packets)
  {
    var frame = new WeaponFrameData { Name = "Test Frame", Packets = [] };
    if (packets.Length == 0)
      frame.Packets.Add(new DamagePacketData());
    foreach ((Element element, float multiplier) in packets)
      frame.Packets.Add(new DamagePacketData { Element = element, Multiplier = multiplier });
    return frame;
  }

  public static Weapon MakeWeapon(string name, int damage = 5, int range = 10)
  {
    return new MeleeWeapon(new WeaponData
    {
      Name = name,
      Description = $"{name} weapon",
      Frame = MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      RangeStat = new RangeStat { BaseValue = range },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
    });
  }

  public static AmmunitionedWeaponData MakeAmmoWeaponData(string name, int magazine = 6, int damage = 5, int range = 10)
  {
    return new AmmunitionedWeaponData
    {
      Name = name,
      Description = $"{name} weapon",
      Frame = MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      RangeStat = new RangeStat { BaseValue = range },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
      AmmunitionStat = new AmmunitionStat { BaseValue = magazine },
      DefaultAmmoData = new Ammunition(),
    };
  }

  public static AmmunitionedWeapon MakeAmmoWeapon(string name, int magazine = 6, int damage = 5, int range = 10)
  {
    return new AmmunitionedWeapon(MakeAmmoWeaponData(name, magazine, damage, range));
  }

  public static ItemWith<ThrowableCapability> MakeThrowable(string name, int throwRange = 4, int actionPointCost = 1, bool consumesOnUse = true)
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = name,
      Description = $"{name} throwable",
      Capabilities = [new ThrowableCapabilityData { ThrowRange = throwRange, ActionPointCost = actionPointCost, ConsumesOnUse = consumesOnUse }],
    });
    return item.With<ThrowableCapability>().RequireSome();
  }

  public static BattleSession MakeSession(
    Vector3I dimensions,
    IEnumerable<Faction> globalFactionOrder,
    IHitChanceCalculator hitChanceCalculator = null,
    int? randomSeed = null,
    Option<Faction> playerFaction = default)
  {
    return MakeSession(new BattleBoardState(dimensions), globalFactionOrder, hitChanceCalculator, randomSeed, playerFaction);
  }

  public static BattleSession MakeSession(
    BattleBoardState board,
    IEnumerable<Faction> globalFactionOrder,
    IHitChanceCalculator hitChanceCalculator = null,
    int? randomSeed = null,
    Option<Faction> playerFaction = default)
  {
    return new BattleSession(board, globalFactionOrder, hitChanceCalculator, randomSeed, playerFaction);
  }
}
