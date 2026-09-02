using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Items.Capabilities;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Stats;
using FunProject.Strategic;
using FunProject.Weapons;

namespace FunProject.Tests;

internal static class GeoscapeTestFactory
{
  public static RegionData MakeRegion(string name)
  {
    return new RegionData
    {
      Name = name,
      FlavorText = $"{name} flavor text.",
    };
  }

  public static GeoscapeSession MakeSession(
    RegionData[]? regions = null,
    ScheduledEventData[]? timeline = null)
  {
    return new GeoscapeSession(new CampaignGameState(MakeStart(regions, timeline)));
  }

  public static GeoscapeEventDefinition MakeEvent(
    string title,
    GeoscapeEventKind kind = GeoscapeEventKind.Plot,
    string targetRegionName = "",
    int expiresAfterTicks = -1)
  {
    return new GeoscapeEventDefinition
    {
      Kind = kind,
      Title = title,
      Description = $"{title} description.",
      ExpiresAfterTicks = expiresAfterTicks,
      TargetRegionName = targetRegionName,
    };
  }

  public static ScheduledEventData MakeScheduled(int atTick, GeoscapeEventDefinition evt)
  {
    return new ScheduledEventData { AtTick = atTick, Event = evt };
  }

  public static CombatantData MakeCombatantData(string name = "Mold")
  {
    return new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = 20 },
      ActionPointsStat = new ActionPointsStat { BaseValue = 8 },
      WillStat = new WillStat { BaseValue = 60 },
      MovementStat = new MovementStat { BaseValue = 14 },
      VisionStat = new VisionStat { BaseValue = 22 },
      AimStat = new AimStat { BaseValue = 60 },
      ModSlotCount = 2,
    };
  }

  public static EquippableItemData MakeArmorData(string name = "Test Vest") => new()
  {
    Name = name,
    Capabilities = [new ArmorCapabilityData { ArmorStat = new BaseArmorStat { BaseValue = 2 } }],
  };

  public static WeaponFrameData MakeFrame() => new()
  {
    Name = "Test Frame",
    Packets = [new DamagePacketData()],
  };

  public static FirearmWeaponData MakeFirearmData(string name = "Test Pistol") => new()
  {
    Name = name,
    Frame = MakeFrame(),
    DamageStat = new DamageStat { BaseValue = 4 },
    RangeStat = new RangeStat { BaseValue = 6 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
    AmmunitionStat = new AmmunitionStat { BaseValue = 6 },
    DefaultAmmoData = new Ammunition { Name = "Test Rounds" },
  };

  public static AmmunitionedWeaponData MakeAmmunitionedData(string name = "Test Rifle") => new()
  {
    Name = name,
    Frame = MakeFrame(),
    DamageStat = new DamageStat { BaseValue = 5 },
    RangeStat = new RangeStat { BaseValue = 8 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
    AmmunitionStat = new AmmunitionStat { BaseValue = 8 },
    DefaultAmmoData = new Ammunition { Name = "Test Rounds" },
  };

  public static WeaponData MakePlainWeaponData(string name = "Test Weapon") => new()
  {
    Name = name,
    Frame = MakeFrame(),
    DamageStat = new DamageStat { BaseValue = 4 },
    RangeStat = new RangeStat { BaseValue = 6 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
  };

  public static RosterEntryData MakeEntry(string displayName = "", CombatantData? unit = null)
  {
    return new RosterEntryData
    {
      Unit = unit ?? MakeCombatantData(),
      DisplayName = displayName,
    };
  }

  public static CampaignStartData MakeStart(
    RegionData[]? regions = null,
    ScheduledEventData[]? timeline = null,
    RosterEntryData[]? roster = null,
    ArmoryEntryData[]? armory = null,
    ModStockEntryData[]? modStock = null)
  {
    return new CampaignStartData
    {
      Name = "Test Campaign",
      Map = new GeoscapeMapData { Regions = regions ?? [], Timeline = timeline ?? [] },
      PlayerFaction = new FactionData { Name = "Test Faction" },
      StartingRoster = roster ?? [],
      Armory = armory ?? [],
      ModStock = modStock ?? [],
    };
  }
}
