using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>Equipment selections for one spawned unit.</summary>
public sealed record UnitLoadout(
  Combatant Combatant,
  Option<Weapon> Weapon = default,
  Option<ItemWith<ArmorCapability>> Armor = default)
{
  /// <summary>Stat contributions supplied with the loadout (e.g. deployment-time
  /// campaign condition penalties). Empty by default; standalone battles need none.</summary>
  public IReadOnlyList<StatMod> StatMods { get; init; } = [];
}

/// <summary>One spawned unit at an explicit board cell.</summary>
public sealed record UnitPlacement(UnitLoadout Loadout, Vector3I Position);

/// <summary>One special board object at an explicit board cell.</summary>
public sealed record ObjectPlacement(BattleSpecialObjectData Data, Vector3I Position);

/// <summary>One battle side: its faction, its objectives, and its positioned units.
/// List order across sides is the global turn order; unit order within a side is retained.</summary>
public sealed record BattleSideSetup(
  Faction Faction,
  IReadOnlyList<ObjectiveData> Objectives,
  IReadOnlyList<UnitPlacement> Units);

/// <summary>Campaign-supplied player deployment: the runtime faction reference plus its
/// explicit loadouts, in deployment order. Every combatant must already belong to the faction.</summary>
public sealed record PlayerDeployment(
  Faction Faction,
  IReadOnlyList<UnitLoadout> Loadouts);

/// <summary>The concrete launch description: one selected map, ordered sides, and an
/// already-resolved seed. Objects/systems default to empty; explicit null is a caller error.</summary>
public sealed record BattleSetup(
  BattleMapData Map,
  IReadOnlyList<BattleSideSetup> Sides,
  int Seed,
  Option<Faction> PlayerFaction = default)
{
  public IReadOnlyList<ObjectPlacement> Objects { get; init; } = [];

  public IReadOnlyList<BattleTypeSystemData> Systems { get; init; } = [];

  /// <summary>The pooled scene the map was extracted from, when this setup was resolved from
  /// a battle type. Hand-built setups carry None; the runtime itself stays scene-independent.</summary>
  public Option<Godot.PackedScene> MapScene { get; init; } = default;
}

/// <summary>Typed reasons a battle request/setup can fail before the session starts.</summary>
public enum BattleSetupFailureReason
{
  /// <summary>A side was missing its required objective list.</summary>
  MissingObjective,
  /// <summary>A referenced faction was not part of the battle's side list.</summary>
  UnknownFaction,
  /// <summary>The authored battle type had no candidate maps to choose from.</summary>
  EmptyMapPool,
  /// <summary>A pooled map scene did not root a BattleMap carrying map data.</summary>
  MapSceneInvalid,
  /// <summary>A unit spawn cell was out of bounds or not occupiable.</summary>
  SpawnCellUnavailable,
  /// <summary>Two units were assigned to the same spawn cell.</summary>
  DuplicateSpawnCell,
  /// <summary>A unit's owning faction did not match its containing side or deployment faction.</summary>
  FactionMismatch,
  /// <summary>The chosen map did not have enough spawn cells for a side's roster.</summary>
  SpawnSlotShortfall,
  /// <summary>An object placement cell was out of bounds, occupied, or not occupiable.</summary>
  ObjectCellUnavailable,
  /// <summary>Two objects were assigned to the same placement cell.</summary>
  DuplicateObjectCell,
  /// <summary>A participating side finished preparation without a living, conscious unit.</summary>
  NoConsciousUnits,
}

/// <summary>A typed setup failure with a human-readable explanation.</summary>
public sealed record BattleSetupFailure(BattleSetupFailureReason Reason, string Message);
