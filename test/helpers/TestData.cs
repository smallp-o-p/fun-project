#nullable disable warnings
using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Combatants.Conditions;
using FunProject.Core;
using FunProject.Dialogue;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Progression;
using FunProject.Research;
using FunProject.Stats;
using FunProject.Strategic;
using FunProject.Weapons;
using Godot;
using System.Collections.Generic;
// RosterEntryData exists in both FunProject.Battle and FunProject.GameState; the geoscape
// roster builders use the GameState one.
using RosterEntryData = FunProject.GameState.RosterEntryData;

namespace FunProject.Tests;

// Consolidated stateless test-data construction, merged from BattleTestFactory,
// GeoscapeTestFactory, and ProgressionTestFactory. Pure builders only: no session/runtime/
// executor bring-up and no cached resources — every returned graph is fresh, and default rank
// tables are built per graph instead of sharing a mutable ladder.
internal static class TestData
{
  // Hermetic default for test-built combatants: test data carries its own ladder instead
  // of sharing the mutable res://resources/ranks.tres table across graphs. Three full-factor
  // rungs so gains accrue without immediately hitting max.
  public static RankTableData MakeRankTable(params RankLevelData[] levels)
  {
    var table = new RankTableData();
    RankLevelData[] selected = levels.Length == 0
      ? [new() { Name = "Rookie", GainFactorPercent = 100 },
         new() { Name = "Squaddie", GainFactorPercent = 100 },
         new() { Name = "Corporal", GainFactorPercent = 100 }]
      : levels;
    foreach (RankLevelData level in selected)
      table.Levels.Add(level);
    return table;
  }

  public static CombatantData MakeCombatantData(
    string name = "Mold",
    int health = 20,
    int actionPoints = 4,
    int movement = 12,
    int vision = 20,
    int aim = 65,
    int modSlotCount = 0,
    IEnumerable<Buff> buffs = null,
    int will = 50,
    RankTableData rankTable = null)
  {
    var data = new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = health },
      ActionPointsStat = new ActionPointsStat { BaseValue = actionPoints },
      WillStat = new WillStat { BaseValue = will },
      MovementStat = new MovementStat { BaseValue = movement },
      VisionStat = new VisionStat { BaseValue = vision },
      AimStat = new AimStat { BaseValue = aim },
      ModSlotCount = modSlotCount,
      RankTable = rankTable ?? MakeRankTable(),
    };
    foreach (Buff buff in buffs ?? [])
      data.InnateBuffs.Add(buff);
    return data;
  }

  public static Combatant MakeCombatant(
    string name,
    Faction faction,
    int health = 20,
    int actionPoints = 4,
    int movement = 12,
    int vision = 20,
    int aim = 65,
    int modSlotCount = 0,
    IEnumerable<Buff> buffs = null,
    int will = 50,
    RankTableData rankTable = null)
  {
    return new Combatant(
      MakeCombatantData(name, health, actionPoints, movement, vision, aim,
        modSlotCount, buffs, will, rankTable),
      faction);
  }

  public static Faction MakeFaction(string name)
  {
    return new Faction(new FactionData
    {
      Name = name,
      Description = $"{name} faction"
    });
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

  // Melee reach defaults to 2: under Euclidean grid distance that covers the eight adjacent
  // tiles (a diagonal is √2) that melee is meant to threaten.
  public static WeaponData MakeWeaponData(int damage = 10, int critChance = 5, int range = 2, WeaponFrameData frame = null, string name = "Name") =>
    new()
    {
      Name = name,
      Frame = frame ?? MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = critChance },
      RangeStat = new RangeStat { BaseValue = range },
    };

  public static Weapon MakeWeapon(string name, int damage = 5, int range = 10, WeaponFrameData frame = null, params Buff[] grantedBuffs)
  {
    var data = MakeWeaponData(damage: damage, critChance: 0, range: range, frame: frame, name: name);
    data.Description = $"{name} weapon";
    if (grantedBuffs.Length > 0)
    {
      var grant = new BuffGrantCapabilityData();
      foreach (Buff buff in grantedBuffs)
        grant.Buffs.Add(buff);
      data.Capabilities = [grant];
    }

    return new MeleeWeapon(data);
  }

  public static Weapon MakeStunWeapon(string name = "Stunner", int damage = 5)
    => MakeWeapon(name, damage: damage, frame: new WeaponFrameData
    {
      Packets = [new DamagePacketData { Kind = DamageKind.Stun }],
    });

  // A base Weapon whose single packet carries an authored status spec, applied on hit.
  public static Weapon MakeStatusWeapon(StatusEffectSpecData status, int damage = 3, Element element = Element.Kinetic)
  {
    var frame = new WeaponFrameData { Name = "Status Frame", Packets = [] };
    frame.Packets.Add(new DamagePacketData { Element = element, Multiplier = 1f, Status = status });
    var data = MakeWeaponData(damage: damage, critChance: 0, range: 10, frame: frame, name: "Status Weapon");
    data.Description = "Applies a status on hit";
    return new Weapon(data);
  }

  public static FirearmWeaponData MakeFirearmWeaponData(
    string name = "Test Firearm",
    int damage = 10,
    int critChance = 5,
    int range = 10,
    int magazine = 12,
    WeaponFrameData frame = null,
    Ammunition ammo = null,
    int modSlots = 0) =>
    new()
    {
      Name = name,
      Frame = frame ?? MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = critChance },
      RangeStat = new RangeStat { BaseValue = range },
      AmmunitionStat = new AmmunitionStat { BaseValue = magazine },
      DefaultAmmoData = ammo,
      Capabilities = modSlots > 0 ? [new ModSlotsCapabilityData { SlotCount = modSlots }] : [],
    };

  public static AmmunitionedWeaponData MakeAmmoWeaponData(
    string name,
    int magazine = 6,
    int damage = 5,
    int range = 10,
    int critChance = 0,
    Ammunition ammo = null,
    string description = null)
  {
    return new AmmunitionedWeaponData
    {
      Name = name,
      Description = description ?? $"{name} weapon",
      Frame = MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      RangeStat = new RangeStat { BaseValue = range },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = critChance },
      AmmunitionStat = new AmmunitionStat { BaseValue = magazine },
      DefaultAmmoData = ammo ?? new Ammunition(),
    };
  }

  public static AmmunitionedWeapon MakeAmmoWeapon(string name, int magazine = 6, int damage = 5, int range = 10)
  {
    return new AmmunitionedWeapon(MakeAmmoWeaponData(name, magazine, damage, range));
  }

  public static EquippableItemData MakeArmorData(
    string name,
    int armor = 10,
    Element element = Element.Kinetic,
    int regenDelayTurns = 0,
    int regenPerTurn = 0) => new()
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
    };

  public static ItemWith<ArmorCapability> MakeArmor(
    string name,
    int armor = 10,
    Element element = Element.Kinetic,
    int regenDelayTurns = 0,
    int regenPerTurn = 0)
  {
    return new EquippableItem(MakeArmorData(name, armor, element, regenDelayTurns, regenPerTurn))
      .With<ArmorCapability>().RequireSome();
  }

  public static ItemWith<ThrowableCapability> MakeGrenade(
    string name,
    int throwRange = 4,
    int actionPointCost = 1,
    int blastRadius = 1,
    params BattleEffectData[] effects)
  {
    var blastEffects = new Godot.Collections.Array<BattleEffectData>();
    foreach (BattleEffectData effect in effects)
      blastEffects.Add(effect);

    var item = new EquippableItem(new EquippableItemData
    {
      Name = name,
      Description = $"{name} grenade",
      Capabilities =
      [
        new ThrowableCapabilityData { ThrowRange = throwRange, ActionPointCost = actionPointCost, ConsumesOnUse = true },
        new BlastCapabilityData { BlastRadius = blastRadius, Effects = blastEffects },
        new ChargesCapabilityData { MaxCharges = 1 },
      ],
    });
    return item.With<ThrowableCapability>().RequireSome();
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

  public static ItemWith<ChargesCapability> MakeUsableItem(string name, int maxCharges = 1)
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = name,
      Description = $"{name} usable item",
      Capabilities = [new ChargesCapabilityData { MaxCharges = maxCharges }],
    });
    return item.With<ChargesCapability>().RequireSome();
  }

  // Bare armory stock entries: no capabilities, so withdrawal materializes plain items.
  public static EquippableItemData MakeItemData(string name = "Item", bool unlimited = false,
    uint manufacturingDays = 1) => new()
    {
      Name = name,
      UnlimitedStock = unlimited,
      ManufacturingDurationDays = manufacturingDays,
    };

  public static MultiStatMod MakeMod(string name, bool unlimited = false) =>
    new() { Name = name, UnlimitedStock = unlimited };

  public static DamageOverTimeStatusSpecData MakeBurn(
    int duration = 2,
    int tickDamage = 2,
    Element tickElement = Element.Thermal,
    int applyChancePercent = 100,
    bool requiresHealthDamage = false,
    string name = "Burn") =>
    new()
    {
      Name = name,
      DurationTurns = duration,
      TickDamage = tickDamage,
      TickElement = tickElement,
      ApplyChancePercent = applyChancePercent,
      RequiresHealthDamage = requiresHealthDamage,
    };

  public static ImmobilizeStatusSpecData MakeStun(
    int duration = 1,
    int applyChancePercent = 100,
    bool requiresHealthDamage = false) =>
    new()
    {
      Name = "Stun",
      DurationTurns = duration,
      ApplyChancePercent = applyChancePercent,
      RequiresHealthDamage = requiresHealthDamage,
    };

  public static Buff MakeBuff(
    string name,
    BuffCondition condition,
    StatMod[] statMods = null,
    DamageBundleMod[] damageMods = null)
  {
    var buff = new Buff
    {
      Name = name,
      Description = $"{name} buff",
      Condition = condition,
    };
    foreach (StatMod statMod in statMods ?? [])
      buff.StatMods.Add(statMod);
    foreach (DamageBundleMod damageMod in damageMods ?? [])
      buff.DamageMods.Add(damageMod);
    return buff;
  }

  public static BattleMapTileData FloorTile() => new() { Walkable = true };

  public static BattleMapTileData WallTile() => new() { Walkable = false, BlocksLineOfSight = true };

  public static BattleMapTileData SpawnTile(int slot) => new() { SpawnFactionSlot = slot };

  public static BattleMapAuthoring MakeMapAuthoring(Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData> cells)
  {
    var library = new MeshLibrary();
    var palette = new BattleTilePalette { MeshLibrary = library };
    var map = new BattleMapAuthoring { Palette = palette, MeshLibrary = library, CellSize = Vector3.One, CellCenterY = false };
    int item = 0;
    foreach (var (cell, tile) in cells)
    {
      library.CreateItem(item);
      library.SetItemName(item, item.ToString());
      library.SetItemMesh(item, new BoxMesh());
      palette.Brushes[item.ToString()] = tile;
      map.SetCellItem(cell, item++);
    }
    return map;
  }

  // Takes runtime Vector3I coordinates (X = width, Y = levels/height, Z = depth) and converts
  // them to Godot.Vector3I dictionary keys when building the authored map.
  public static BattleMapData MakeMapData(
    Vector3I dimensions,
    params (Vector3I Cell, BattleMapTileData Tile)[] tiles)
  {
    var dict = new Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData>();
    foreach ((Vector3I cell, BattleMapTileData tile) in tiles)
      dict[new Godot.Vector3I(cell.X, cell.Y, cell.Z)] = tile;

    return new BattleMapData
    {
      Dimensions = new Godot.Vector3I(dimensions.X, dimensions.Y, dimensions.Z),
      Tiles = dict,
    };
  }

  // An all-walkable, all-floor height-one board; every cell is an authored tile.
  public static BattleMapData MakeOpenBattleMap(int width = 4, int depth = 4)
  {
    List<(Vector3I Cell, BattleMapTileData Tile)> tiles = [];
    for (int x = 0; x < width; x++)
      for (int z = 0; z < depth; z++)
        tiles.Add((new Vector3I(x, 0, z), FloorTile()));
    return MakeMapData(new Vector3I(width, 1, depth), [.. tiles]);
  }

  // Packs the map into a BattleMap-rooted scene, the authored pool's storage shape; the
  // prototype node is freed after packing so only the scene survives. Runs under the Godot
  // runtime covered by the RequireGodotRuntime suites.
  public static PackedScene MakeMapScene(BattleMapData map)
  {
    var battleMap = new BattleMap { MapData = map, UsedPalette = new BattleTilePalette { MeshLibrary = null } };
    var scene = new PackedScene();
    Error error = scene.Pack(battleMap);
    battleMap.Free();
    if (error != Error.Ok)
      throw new System.InvalidOperationException($"Test map scene packing failed: {error}");
    return scene;
  }

  // Concrete setup for direct factory tests: fresh open 4x1x4 board, one unit per side at
  // (0,0,0)/(3,0,3), fixed seed 7, optional designated player faction. Pure: callers add
  // objects/systems and keep every supplied reference.
  public static BattleSetup MakeBattleSetup(Faction playerFaction, Faction enemyFaction,
    UnitLoadout playerUnit, UnitLoadout enemyUnit,
    IReadOnlyList<ObjectiveData> playerObjectives, IReadOnlyList<ObjectiveData> enemyObjectives,
    Option<Faction> designatedPlayer = default)
  {
    return new BattleSetup(
      MakeOpenBattleMap(),
      [
        new BattleSideSetup(playerFaction, playerObjectives,
          [new UnitPlacement(playerUnit, new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemyFaction, enemyObjectives,
          [new UnitPlacement(enemyUnit, new Vector3I(3, 0, 3))]),
      ],
      Seed: 7,
      PlayerFaction: designatedPlayer);
  }

  public static BattleSpecialObjectData MakeObject(
    string name = "Crate", int? health = null,
    params SpecialObjectCapabilityData[] capabilities)
  {
    var data = new BattleSpecialObjectData { Name = name };
    if (health is int value)
      data.Capabilities.Add(new ObjectHealthCapabilityData
      {
        HealthStat = new HealthStat { BaseValue = value },
      });
    foreach (SpecialObjectCapabilityData capability in capabilities)
      data.Capabilities.Add(capability);
    return data;
  }

  // Two-faction duel on a 2x1x1 map: one spawn cell per side, one unit per side (health 20,
  // aim 65, damage-1 range-10 weapon), one inert objective each.
  public static BattleTypeData MakeDuelBattleType()
  {
    var type = new BattleTypeData { Name = "Setup duel" };
    type.MapPool.Add(MakeMapScene(MakeMapData(new Vector3I(2, 1, 1),
      (new Vector3I(0, 0, 0), SpawnTile(0)),
      (new Vector3I(1, 0, 0), SpawnTile(1)))));
    string[] names = ["Player", "Enemy"];
    foreach (string name in names)
    {
      var side = new FactionDeploymentData { Faction = new FactionData { Name = name } };
      side.Roster.Add(new FunProject.Battle.RosterEntryData
      {
        Combatant = MakeCombatantData(name, health: 20, aim: 65),
        Weapon = MakeWeaponData(damage: 1, critChance: 0, range: 10),
      });
      side.Objectives.Add(new FakeObjectiveData());
      type.Factions.Add(side);
    }
    return type;
  }

  public static RegionData MakeRegion(string name)
  {
    return new RegionData
    {
      Name = name,
      FlavorText = $"{name} flavor text.",
    };
  }

  public static GeoscapeEventDefinition MakeEvent(
    string title,
    GeoscapeEventKind kind = GeoscapeEventKind.Plot,
    string targetRegionName = "",
    int expiresAfterTicks = -1,
    bool allowUnfitDeployment = false,
    DialogueSequenceData? dialogue = null)
  {
    return new GeoscapeEventDefinition
    {
      Kind = kind,
      Title = title,
      Description = $"{title} description.",
      ExpiresAfterTicks = expiresAfterTicks,
      TargetRegionName = targetRegionName,
      AllowUnfitDeployment = allowUnfitDeployment,
      Dialogue = dialogue,
    };
  }

  public static ScheduledEventData MakeScheduled(int atTick, GeoscapeEventDefinition evt)
  {
    return new ScheduledEventData { AtTick = atTick, Event = evt };
  }

  public static RosterEntryData MakeEntry(string displayName = "", CombatantData unit = null)
  {
    return new RosterEntryData
    {
      Unit = unit ?? MakeCombatantData("Mold", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60),
      DisplayName = displayName,
    };
  }

  // Explicit condition baselines (100 health keeps damage percentages on whole tiers) so
  // condition labels, countdowns, and penalized stats have hand-checkable values.
  public static RosterEntryData MakeConditionEntry(string name)
    => MakeEntry(name, MakeCombatantData(name, health: 100, actionPoints: 8,
      movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 50));

  public static CaptiveEntryData MakeCaptiveEntry(
    string displayName = "",
    CombatantData unit = null,
    FactionData faction = null)
  {
    return new CaptiveEntryData
    {
      Unit = unit ?? MakeCombatantData("Grunt"),
      Faction = faction ?? new FactionData { Name = "Enemy Faction" },
      DisplayName = displayName,
    };
  }

  public static CampaignStartData MakeStart(
    RegionData[] regions = null,
    ScheduledEventData[] timeline = null,
    RosterEntryData[] roster = null,
    EquippableItemData[] armory = null,
    EquippableMod[] modStock = null,
    EquippableItemData[] manufacturableItems = null,
    ResearchProject[] researchProjects = null,
    CaptiveEntryData[] captives = null,
    CombatantConditionRulesData? conditionRules = null)
  {
    return new CampaignStartData
    {
      Name = "Test Campaign",
      Map = new GeoscapeMapData { Regions = regions ?? [], Timeline = timeline ?? [] },
      PlayerFaction = new FactionData { Name = "Test Faction" },
      StartingRoster = roster ?? [],
      Armory = armory ?? [],
      ModStock = modStock ?? [],
      ManufacturableItems = manufacturableItems ?? [],
      ResearchProjects = researchProjects ?? [],
      StartingCaptives = captives ?? [],
      ConditionRules = conditionRules,
    };
  }

  public static ResearchProject MakeResearch(
    string name = "Research", uint days = 1,
    ResearchCondition condition = null,
    EquippableItemData[] manufacturingUnlocks = null)
    => new()
    {
      Name = name,
      DurationDays = days,
      Condition = condition ?? new AlwaysResearchCondition(),
      ManufacturingUnlocks = manufacturingUnlocks ?? [],
    };

  public static SkillPathData MakePath(string name, params SkillUpgradeStepData[] steps)
  {
    var path = new SkillPathData { Name = name, Description = $"{name} path" };
    foreach (SkillUpgradeStepData step in steps)
      path.Steps.Add(step);
    return path;
  }

  public static SkillUpgradeStepData MakeStep(int cost, params UpgradeEffectData[] effects)
  {
    var step = new SkillUpgradeStepData { Cost = cost };
    foreach (UpgradeEffectData effect in effects)
      step.Effects.Add(effect);
    return step;
  }

  public static StatModUpgradeEffectData MakeStatModEffect(params StatMod[] mods)
  {
    var effect = new StatModUpgradeEffectData();
    foreach (StatMod mod in mods)
      effect.StatMods.Add(mod);
    return effect;
  }

  public static BuffGrantUpgradeEffectData MakeBuffGrantEffect(params Buff[] buffs)
  {
    var effect = new BuffGrantUpgradeEffectData();
    foreach (Buff buff in buffs)
      effect.Buffs.Add(buff);
    return effect;
  }

  public static AbilityGrantUpgradeEffectData MakeAbilityGrantEffect(string abilityName)
    => new() { Ability = new AbilityData { Name = abilityName } };
}
