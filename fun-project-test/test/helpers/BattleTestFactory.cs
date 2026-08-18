using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;
using System.Collections.Generic;

namespace FunProject.Tests;

// A placement description for StartRuntime: which faction/combatant spawns where, with an
// optional equipped weapon. Faction order is derived from the placements' first-appearance order.
internal readonly record struct StartPlacement(
  Faction Faction,
  Combatant Combatant,
  Vector3I Position,
  Option<Weapon> Weapon = default);

internal static class BattleTestFactory
{
  public static Combatant MakeCombatant(
    string name,
    Faction faction,
    int health = 20,
    int actionPoints = 4,
    int movement = 12,
    int vision = 20,
    int aim = 65,
    int modSlotCount = 0,
    IEnumerable<BuffData> buffs = null)
  {
    var data = new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = health },
      ActionPointsStat = new ActionPointsStat { BaseValue = actionPoints },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = movement },
      VisionStat = new VisionStat { BaseValue = vision },
      AimStat = new AimStat { BaseValue = aim },
      ModSlotCount = modSlotCount,
    };
    foreach (BuffData buff in buffs ?? [])
      data.InnateBuffs.Add(buff);

    return new Combatant(data, faction);
  }

  public static Faction MakeFaction(string name)
  {
    return new Faction(new FactionData
    {
      Name = name,
      Description = $"{name} faction"
    });
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

  public static Weapon MakeWeapon(string name, int damage = 5, int range = 10, WeaponFrameData frame = null, params BuffData[] grantedBuffs)
  {
    var data = new WeaponData
    {
      Name = name,
      Description = $"{name} weapon",
      Frame = frame ?? MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      RangeStat = new RangeStat { BaseValue = range },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
    };
    if (grantedBuffs.Length > 0)
    {
      var grant = new BuffGrantCapabilityData();
      foreach (BuffData buff in grantedBuffs)
        grant.Buffs.Add(buff);
      data.Capabilities = [grant];
    }

    return new MeleeWeapon(data);
  }

  // A base Weapon whose single packet carries an authored status spec, applied on hit.
  public static Weapon MakeStatusWeapon(StatusEffectSpecData status, int damage = 3, Element element = Element.Kinetic)
  {
    var frame = new WeaponFrameData { Name = "Status Frame", Packets = [] };
    frame.Packets.Add(new DamagePacketData { Element = element, Multiplier = 1f, Status = status });
    return new Weapon(new WeaponData
    {
      Name = "Status Weapon",
      Description = "Applies a status on hit",
      Frame = frame,
      DamageStat = new DamageStat { BaseValue = damage },
      RangeStat = new RangeStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
    });
  }

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

  public static BuffData MakeBuff(
    string name,
    BuffConditionData condition,
    StatMod[] statMods = null,
    DamageBundleMod[] damageMods = null)
  {
    var buff = new BuffData
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

  public static WeaponData MakeWeaponData(int damage = 10, int critChance = 5, int range = 1, WeaponFrameData frame = null) =>
    new()
    {
      Frame = frame ?? MakeFrame(),
      DamageStat = new DamageStat { BaseValue = damage },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = critChance },
      RangeStat = new RangeStat { BaseValue = range },
    };

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

  // Runtime owning the session's ONLY executor (public ctor). Never mix with the
  // ExecutorFor session helpers on the same session — a second executor would
  // double-register the default systems (statuses ticking twice).
  public static BattleRuntime RuntimeFor(BattleSession session)
    => new(session);

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

  // Production-path bring-up: builds a BattleSetup (one FakeObjective per distinct faction, in
  // first-appearance order across the placements), starts it via BattleFactory, and returns the
  // live runtime. Accepts a prebuilt board so callers can pre-mutate walkability/cover.
  public static BattleRuntime StartRuntime(BattleBoardState board, params StartPlacement[] placements)
  {
    List<Faction> factionOrder = placements.AsValueEnumerable().Select(placement => placement.Faction).Distinct().ToList();
    List<UnitPlacement> unitPlacements = placements
      .AsValueEnumerable().Select(placement => new UnitPlacement(new UnitLoadout(placement.Combatant, placement.Weapon), placement.Position))
      .ToList();
    var objectives = factionOrder.AsValueEnumerable().ToDictionary(
      faction => faction,
      faction => (IReadOnlyList<ObjectiveData>)new ObjectiveData[] { new FakeObjectiveData() });

    return BattleFactory.Start(new BattleSetup(board, factionOrder, unitPlacements, objectives)).Match(
      Right: runtime => runtime,
      Left: failure => throw new System.InvalidOperationException($"StartRuntime failed: {failure.Reason}: {failure.Message}"));
  }

  public static BattleRuntime StartRuntime(Vector3I dimensions, params StartPlacement[] placements) =>
    StartRuntime(new BattleBoardState(dimensions), placements);
}
