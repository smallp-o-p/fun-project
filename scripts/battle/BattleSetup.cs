using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using System.Collections.Generic;

namespace FunProject.Battle;

// Gear for one unit, mirroring SpawnUnit's optional weapon/armor. Position-free so it is reused
// by the board-level core (paired with a position) and the map convenience (position derived).
public sealed record UnitLoadout(
  Combatant Combatant,
  Option<Weapon> Weapon = default,
  Option<ItemWith<ArmorCapability>> Armor = default);

// One spawned unit at an explicit board cell.
public sealed record UnitPlacement(UnitLoadout Loadout, Vector3I Position);

// Board-level core input.
public sealed record BattleSetup(
  BattleBoardState Board,
  IReadOnlyList<Faction> FactionOrder,
  IReadOnlyList<UnitPlacement> Placements,
  IReadOnlyDictionary<Faction, IReadOnlyList<ObjectiveData>> Objectives,
  int? Seed = null,
  Option<Faction> PlayerFaction = default,
  IHitChanceCalculator? HitChance = null);

// Map-level convenience input. Slot index = BattleMapTileData.SpawnFactionSlot, which is the
// index into FactionOrder (slot i maps to FactionOrder[i]).
public sealed record MapBattleSetup(
  BattleMapData Map,
  IReadOnlyList<Faction> FactionOrder,
  IReadOnlyDictionary<int, IReadOnlyList<UnitLoadout>> RostersBySlot,
  IReadOnlyDictionary<Faction, IReadOnlyList<ObjectiveData>> Objectives,
  int? Seed = null,
  Option<Faction> PlayerFaction = default,
  IHitChanceCalculator? HitChance = null);

public enum BattleSetupFailureReason
{
  MissingObjective,
  UnknownFaction,
  SpawnCellUnavailable,
  DuplicateSpawnCell,
  InvalidSpawnSlot,
  SlotFactionMismatch,
  SpawnSlotShortfall,
}

public sealed record BattleSetupFailure(BattleSetupFailureReason Reason, string Message);
