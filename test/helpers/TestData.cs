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
using FunProject.Models;
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

  public static BattleAnnotationGrid Annotate(Node3D asset, BattleFootprintData footprint, Vector3? cellSize = null)
  {
    var grid = new BattleAnnotationGrid { Name = "Annotations", Annotations = footprint, CellSize = cellSize ?? Vector3.One };
    ((BoxMesh)grid.MeshLibrary.GetItemMesh(0)).Size = grid.CellSize * 0.96f;
    asset.AddChild(grid);
    grid.Owner = asset;
    foreach (var cell in footprint.Cells.Keys) grid.SetCellItem(cell, 0);
    grid.Baseline = grid.CaptureLayout();
    return grid;
  }

  public static BattleFootprintData MakeFloorFootprint(int width, int depth)
  {
    var footprint = new BattleFootprintData();
    for (int x = 0; x < width; x++)
      for (int z = 0; z < depth; z++)
        footprint.Cells[new(x, 0, z)] = new() { HasFloor = true };
    return footprint;
  }

  public static BattleMapTileData FloorTile() => new() { Walkable = true };

  public static BattleMapTileData WallTile() => new() { Walkable = false, BlocksLineOfSight = true };

  public static BattleMapTileData SpawnTile(int slot) => new() { SpawnFactionSlot = slot };

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
  public static PackedScene MakeMapScene(BattleMapData map) =>
    GeoscapeTestScenes.Pack(new BattleMap { MapData = map });

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

  // One surface of `triangleCount` disjoint triangles; triangle t occupies vertices
  // 3t..3t+2 at base position (10t, 0, 0) so compacted vertex data is assertable.
  public static ArrayMesh MakeMaskedSourceMesh(int triangleCount = 2)
  {
    var vertices = new Vector3[triangleCount * 3];
    var indices = new int[triangleCount * 3];
    for (int t = 0; t < triangleCount; t++)
    {
      vertices[3 * t] = new Vector3(10 * t, 0, 0);
      vertices[3 * t + 1] = new Vector3(10 * t + 1, 0, 0);
      vertices[3 * t + 2] = new Vector3(10 * t, 1, 0);
      for (int v = 0; v < 3; v++)
        indices[3 * t + v] = 3 * t + v;
    }

    var mesh = new ArrayMesh();
    var arrays = new Godot.Collections.Array();
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = vertices;
    arrays[(int)Mesh.ArrayType.Index] = indices;
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    return mesh;
  }

  // Two-triangle surface exercising every per-vertex channel stride: normals, colors,
  // skin bones/weights (4- or 8-wide), and one blend shape with vertex + normal deltas.
  public static ArrayMesh MakeSkinSourceMesh(bool eightBoneWeights = false, bool withBlendShape = true)
  {
    if (eightBoneWeights && withBlendShape)
      throw new System.ArgumentException("The fixture never combines 8-bone weights with blend shapes.");
    int stride = eightBoneWeights ? 8 : 4;
    var mesh = new ArrayMesh();
    if (withBlendShape)
      mesh.AddBlendShape("Wide");
    var vertices = new Vector3[6];
    var normals = new Vector3[6];
    var colors = new Color[6];
    var bones = new int[6 * stride];
    var weights = new float[6 * stride];
    for (int v = 0; v < 6; v++)
    {
      vertices[v] = new Vector3(10 * (v / 3) + (v % 3 == 2 ? 0 : v % 3), v % 3 == 2 ? 1 : 0, 0);
      normals[v] = Vector3.Back;
      colors[v] = new Color(v * 0.1f, 0.5f, 1f);
      for (int k = 0; k < stride; k++)
      {
        bones[v * stride + k] = k % 16;
        weights[v * stride + k] = 1f / stride;
      }
    }

    var arrays = new Godot.Collections.Array();
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = vertices;
    arrays[(int)Mesh.ArrayType.Normal] = normals;
    arrays[(int)Mesh.ArrayType.Color] = colors;
    arrays[(int)Mesh.ArrayType.Bones] = bones;
    arrays[(int)Mesh.ArrayType.Weights] = weights;
    arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 1, 2, 3, 4, 5 };
    if (withBlendShape)
    {
      var shapeVertices = new Vector3[6];
      var shapeNormals = new Vector3[6];
      for (int v = 0; v < 6; v++)
      {
        shapeVertices[v] = new Vector3(1, 0, 0);
        shapeNormals[v] = Vector3.Up;
      }
      var shape = new Godot.Collections.Array();
      shape.Resize((int)Mesh.ArrayType.Max);
      shape[(int)Mesh.ArrayType.Vertex] = shapeVertices;
      shape[(int)Mesh.ArrayType.Normal] = shapeNormals;
      mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes: [shape]);
    }
    else if (eightBoneWeights)
    {
      mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
        flags: Mesh.ArrayFormat.FlagUse8BoneWeights);
    }
    else
    {
      mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
    return mesh;
  }

  // Authored mask configuration in the typed ModelMaskConfiguration form: mask
  // names with defaults, triangle masks, default bits, default mesh, and the
  // geometry hash. Mask i hides triangle i; the default bits hide triangle 0.
  public static ModelMaskConfiguration MakeModelMaskConfiguration(ArrayMesh source, int maskCount = 2, long defaultBits = 1)
  {
    if (maskCount is < 1 or > 64)
      throw new System.ArgumentOutOfRangeException(nameof(maskCount), maskCount,
        "Mask fixture configurations need between 1 and 64 masks.");
    if (source.GetSurfaceCount() != 1)
      throw new System.ArgumentException("Mask fixture configurations expect a single-surface source mesh.");

    var masks = new Godot.Collections.Dictionary<string, bool>();
    var triangleFlags = new long[maskCount];
    for (int i = 0; i < maskCount; i++)
    {
      masks[$"Mask{i}"] = false;
      triangleFlags[i] = 1L << i;
    }
    var triangleMasks = new Godot.Collections.Array<long[]> { triangleFlags };

    return new ModelMaskConfiguration
    {
      Masks = masks,
      TriangleMasks = triangleMasks,
      DefaultBits = defaultBits,
      DefaultMesh = ModelMaskGeometry.BuildMesh(source, [triangleFlags], defaultBits),
      GeometryHash = ModelImportValidation.GeometryHash(source),
    };
  }

  // Exercise the same bound-rule selection operation used by the wardrobe.
  public static void SelectMaskRegions(CharacterModel model, NodePath meshPath, params string[] enabled)
  {
    CharacterModel.MaskRuntime mask = model.ResolveMask(meshPath);
    var selected = new SysColGeneric.List<CharacterModel.MaskRuntime.Region>();
    foreach (string name in enabled)
      selected.Add(mask.Regions.AsValueEnumerable().First(region => region.Name == name));
    mask.PrepareSelection(mask.Regions, selected)();
  }

  // Counts the generated triangles a render mesh draws per surface, skipping
  // only the all-hidden convention's degenerate [x, x, x] sentinel triangle; it
  // does not validate the geometry of the triangles it counts.
  public static int VisibleTriangles(ArrayMesh? mesh)
  {
    if (mesh is null)
      throw new System.InvalidOperationException("The mask renders no mesh yet.");
    int visible = 0;
    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
    {
      int[] indices = mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index].AsInt32Array();
      for (int t = 0; t < indices.Length / 3; t++)
      {
        if (indices[3 * t] == indices[3 * t + 1] && indices[3 * t + 1] == indices[3 * t + 2])
          continue;
        visible++;
      }
    }

    return visible;
  }

  // Test inputs simulate the importer's serialized output, not a runtime authoring API.
  public static T ImportedResource<T>(params (string Name, Variant Value)[] fields) where T : Resource, new()
  {
    var resource = new T();
    foreach (var (name, value) in fields)
      resource.Set(name, value);
    return resource;
  }

  private static ModelWardrobeGarment Piece(string path, int variant = -1)
    => ImportedResource<ModelWardrobeGarment>(("_path", new NodePath(path)), ("_variant", variant));

  private static ModelWardrobeMaskRule MaskRule(string path, string name, int index,
    string component, int variant)
    => ImportedResource<ModelWardrobeMaskRule>(("_path", new NodePath(path)), ("_name", new StringName(name)),
      ("_index", index), ("_component", component), ("_variant", variant));

  private static ModelWardrobeComponent Component(params ModelWardrobeGarment[] pieces)
    => ImportedResource<ModelWardrobeComponent>(("_pieces", new Godot.Collections.Array<ModelWardrobeGarment>(pieces)));

  // One imported component with a garment and body mask rule per outfit.
  public static ModelWardrobeConfiguration MakeWardrobeConfiguration()
    => ImportedResource<ModelWardrobeConfiguration>(
      ("_variants", new string[] { "outfit0", "outfit1" }),
      ("_components", new Godot.Collections.Dictionary<string, ModelWardrobeComponent>
      {
        ["Garment"] = Component(Piece("GarmentA", 0), Piece("GarmentB", 1)),
      }),
      ("_masks", new Godot.Collections.Array<ModelWardrobeMaskRule>
      {
        MaskRule("Body", "Mask0", 0, "Garment", 0), MaskRule("Body", "Mask1", 1, "Garment", 1),
      }));

  // Two outfit-independent components cover the same region. Their rules must
  // OR their contributions rather than allowing the last rule to win.
  public static ModelWardrobeConfiguration MakeOverlappingWardrobeConfiguration()
    => ImportedResource<ModelWardrobeConfiguration>(
      ("_variants", new string[] { "outfit0", "outfit1" }),
      ("_components", new Godot.Collections.Dictionary<string, ModelWardrobeComponent>
      {
        ["shirt"] = Component(Piece("GarmentA")),
        ["jacket"] = Component(Piece("GarmentB")),
      }),
      ("_masks", new Godot.Collections.Array<ModelWardrobeMaskRule>
      {
        MaskRule("Body", "Mask0", 0, "shirt", -1), MaskRule("Body", "Mask0", 0, "jacket", -1),
      }));

  // The second mask has no component-associated rule; outfit-wide changes
  // must still reach its componentless rule.
  public static ModelWardrobeConfiguration MakeWardrobeConfigurationWithComponentlessVariantRule(int variant)
  {
    var configuration = MakeWardrobeConfiguration();
    var rules = new Godot.Collections.Array<ModelWardrobeMaskRule>(configuration.Masks);
    rules.Add(MaskRule("AccessoryBody", "Mask1", 1, "", variant));
    configuration.Set("_masks", rules);
    return configuration;
  }

  // Scene-local fixture materials with authored outline toggles and weights.
  public static ShaderMaterial MakeModelMaterial()
  {
    const string code = """
      shader_type spatial;

      uniform bool enabled = true;
      uniform float width_scale = 1.0;
      uniform sampler2D vertex_weights : filter_nearest;
      """;
    var shader = new Shader { Code = code };
    var outline = new ShaderMaterial { Shader = shader, ResourceLocalToScene = true };
    outline.SetMeta("source_weights", new float[] { 1, 1, 1, 1, 1, 1 });
    var material = new ShaderMaterial { Shader = shader, NextPass = outline, ResourceLocalToScene = true };
    return material;
  }

  // Single-triangle mesh carrying the Smile and Blink blend shapes in registration
  // order (Smile = 0, Blink = 1), each with one vertex/normal delta array, following
  // the established skin-source pattern of registering names before the surface.
  public static ArrayMesh MakeExpressionMesh()
  {
    var mesh = new ArrayMesh();
    mesh.AddBlendShape("Smile");
    mesh.AddBlendShape("Blink");
    var arrays = new Godot.Collections.Array();
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 1, 0) };
    arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back };
    arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 1, 2 };
    var smile = new Godot.Collections.Array();
    smile.Resize((int)Mesh.ArrayType.Max);
    smile[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(0, 1, 0), new(1, 1, 0), new(0, 1, 1) };
    smile[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Up, Vector3.Up, Vector3.Up };
    var blink = new Godot.Collections.Array();
    blink.Resize((int)Mesh.ArrayType.Max);
    blink[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(1, 0, 0), new(2, 0, 0), new(1, 0, 1) };
    blink[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Right, Vector3.Right, Vector3.Right };
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes: [smile, blink]);
    return mesh;
  }

  // Deterministic expression clips: one-second looping constants. Neutral carries
  // the face baseline plus the moving skeleton track (body, five degrees around Y
  // at the midpoint) and a static identity head track; the expression clips each
  // hold a single face value so every expression stays neutral-relative.
  public static AnimationLibrary MakeExpressionLibrary()
  {
    var library = new AnimationLibrary();
    library.AddAnimation("neutral", MakeExpressionClip("neutral"));
    library.AddAnimation("smile", MakeExpressionClip("smile"));
    library.AddAnimation("blink", MakeExpressionClip("blink"));
    library.AddAnimation("smile_extra", MakeExpressionClip("smile_extra"));
    return library;
  }

  private static Animation MakeExpressionClip(string name)
  {
    var clip = new Animation { Length = 1, LoopMode = Animation.LoopModeEnum.Linear };
    switch (name)
    {
      case "neutral":
        SetConstantBlendShape(clip, "Face:Smile", 0.2f);
        SetConstantBlendShape(clip, "Face:Blink", 0f);
        Quaternion identity = Quaternion.Identity;
        Quaternion tilted = Quaternion.FromEuler(new Vector3(0, Mathf.DegToRad(5), 0));
        int moving = clip.AddTrack(Animation.TrackType.Rotation3D);
        clip.TrackSetPath(moving, "Skeleton:body");
        clip.RotationTrackInsertKey(moving, 0, identity);
        clip.RotationTrackInsertKey(moving, 0.5, tilted);
        clip.RotationTrackInsertKey(moving, 1, identity);
        int head = clip.AddTrack(Animation.TrackType.Rotation3D);
        clip.TrackSetPath(head, "Skeleton:head");
        clip.RotationTrackInsertKey(head, 0, identity);
        break;
      case "smile":
        SetConstantBlendShape(clip, "Face:Smile", 0.8f);
        break;
      case "blink":
        SetConstantBlendShape(clip, "Face:Blink", 1f);
        break;
      case "smile_extra":
        SetConstantBlendShape(clip, "Face:Smile", 0.5f);
        break;
    }

    return clip;
  }

  private static void SetConstantBlendShape(Animation clip, string path, float value)
  {
    int track = clip.AddTrack(Animation.TrackType.BlendShape);
    clip.TrackSetPath(track, path);
    clip.BlendShapeTrackInsertKey(track, 0, value);
  }

  // Deterministic expression graph: each expression subtracts its own
  // AnimationNodeAnimation reference of the shared neutral clip and feeds the delta
  // into an Add2 filtered to its own face track, chained after the previous
  // expression. Sharing the Animation resource between the reference nodes keeps
  // every delta neutral-relative; the filters keep an expression from subtracting
  // or adding the other face channels.
  public static AnimationNodeBlendTree MakeExpressionTree()
  {
    var graph = new AnimationNodeBlendTree();
    graph.AddNode("Neutral", new AnimationNodeAnimation { Animation = "neutral" });
    AddExpressionChain(graph, "Smile", "smile", "Face:Smile", "Neutral");
    AddExpressionChain(graph, "Blink", "blink", "Face:Blink", "Smile");
    AddExpressionChain(graph, "SmileExtra", "smile_extra", "Face:Smile", "Blink");
    graph.ConnectNode("output", 0, "SmileExtra");
    return graph;
  }

  private static void AddExpressionChain(
    AnimationNodeBlendTree graph, string id, string clipName, string filterPath, string input)
  {
    graph.AddNode($"{id}Clip", new AnimationNodeAnimation { Animation = clipName });
    graph.AddNode($"{id}Neutral", new AnimationNodeAnimation { Animation = "neutral" });
    graph.AddNode($"{id}Delta", new AnimationNodeSub2());
    graph.ConnectNode($"{id}Delta", 0, $"{id}Clip");
    graph.ConnectNode($"{id}Delta", 1, $"{id}Neutral");
    var add = new AnimationNodeAdd2 { FilterEnabled = true };
    add.SetFilterPath(filterPath, true);
    graph.AddNode(id, add);
    graph.ConnectNode(id, 0, input);
    graph.ConnectNode(id, 1, $"{id}Delta");
  }
}
