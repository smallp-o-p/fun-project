using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>Equipment selections for one spawned unit.</summary>
public sealed record UnitLoadout(
  Combatant Combatant,
  Option<Weapon> Weapon = default,
  Option<ItemWith<ArmorCapability>> Armor = default);

/// <summary>One spawned unit at an explicit board cell.</summary>
public sealed record UnitPlacement(UnitLoadout Loadout, Vector3I Position);

/// <summary>One special board object at an explicit board cell.</summary>
public sealed record ObjectPlacement(BattleSpecialObjectData Data, Vector3I Position);

/// <summary>Board-level input for starting a battle session.</summary>
public sealed record BattleSetup(
  BattleBoardState Board,
  IReadOnlyList<Faction> FactionOrder,
  IReadOnlyList<UnitPlacement> Placements,
  IReadOnlyDictionary<Faction, IReadOnlyList<ObjectiveData>> Objectives,
  int? Seed = null,
  Option<Faction> PlayerFaction = default,
  IHitChanceCalculator? HitChance = null,
  IReadOnlyList<ObjectPlacement>? Objects = null);

/// <summary>Map-level convenience input whose spawns are derived from authored slots.</summary>
public sealed record MapBattleSetup(
  BattleMapData Map,
  IReadOnlyList<Faction> FactionOrder,
  IReadOnlyDictionary<int, IReadOnlyList<UnitLoadout>> RostersBySlot,
  IReadOnlyDictionary<Faction, IReadOnlyList<ObjectiveData>> Objectives,
  int? Seed = null,
  Option<Faction> PlayerFaction = default,
  IHitChanceCalculator? HitChance = null);

/// <summary>Typed reasons a battle request/setup can fail before the session starts.</summary>
public enum BattleSetupFailureReason
{
  /// <summary>A faction was missing its required objective list.</summary>
  MissingObjective,
  /// <summary>A referenced faction was not part of the battle's faction order.</summary>
  UnknownFaction,
  /// <summary>The authored battle type had no candidate maps to choose from.</summary>
  EmptyMapPool,
  /// <summary>A unit spawn cell was out of bounds or not occupiable.</summary>
  SpawnCellUnavailable,
  /// <summary>Two units were assigned to the same spawn cell.</summary>
  DuplicateSpawnCell,
  /// <summary>A map spawn slot index was outside the faction-order range.</summary>
  InvalidSpawnSlot,
  /// <summary>A unit's owning faction did not match the slot it was assigned to.</summary>
  SlotFactionMismatch,
  /// <summary>The chosen map did not have enough spawn cells for a slot's roster.</summary>
  SpawnSlotShortfall,
  /// <summary>An object placement cell was out of bounds or not occupiable.</summary>
  ObjectCellUnavailable,
  /// <summary>Two objects were assigned to the same placement cell.</summary>
  DuplicateObjectCell,
}

/// <summary>A typed setup failure with a human-readable explanation.</summary>
public sealed record BattleSetupFailure(BattleSetupFailureReason Reason, string Message);
