using FunProject.Combatants;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

// The production "front door": turns caller-supplied inputs into a started, observable
// BattleRuntime. Lives in the assembly so it can drive the internal session/action primitives.
public static class BattleFactory
{
  public static Either<BattleSetupFailure, BattleRuntime> Start(BattleSetup setup)
  {
    ArgumentNullException.ThrowIfNull(setup);
    ArgumentNullException.ThrowIfNull(setup.Board);
    ArgumentNullException.ThrowIfNull(setup.FactionOrder);
    ArgumentNullException.ThrowIfNull(setup.Placements);
    ArgumentNullException.ThrowIfNull(setup.Objectives);
    if (setup.FactionOrder.Count == 0)
      throw new ArgumentException("FactionOrder must contain at least one faction.", nameof(setup));

    BattleSetupFailure? failure = Validate(setup);
    if (failure is not null)
      return Left<BattleSetupFailure, BattleRuntime>(failure);

    var session = new BattleSession(setup.Board, setup.FactionOrder, setup.HitChance, setup.Seed, setup.PlayerFaction);

    foreach (KeyValuePair<Faction, IReadOnlyList<Objective>> entry in setup.Objectives)
    {
      foreach (Objective objective in entry.Value)
        session.AddObjective(entry.Key, objective);
    }

    var runtime = new BattleRuntime(session);

    foreach (UnitPlacement placement in setup.Placements)
    {
      // This is the mint door for setup input: bounds are proven here, occupancy stays
      // with SpawnUnit (the authority on mutable placement legality).
      Option<BattleBoardState.ValidatedPoint> spawnPointOption = setup.Board.ValidatePoint(placement.Position);
      if (spawnPointOption.IsNone)
      {
        runtime.Dispose();
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.SpawnCellUnavailable,
          $"Spawn cell {placement.Position} for {placement.Loadout.Combatant.Name} is out of bounds."));
      }

      var spawn = new SpawnUnit(
        placement.Loadout.Combatant,
        spawnPointOption.Value(),
        placement.Loadout.Weapon,
        placement.Loadout.Armor);
      try
      {
        // Rejected parameters surface as the executor's invariant-break throw; the only
        // expected cause here is occupancy, already pre-validated as far as setup can know.
        runtime.ExecuteAction(spawn);
      }
      catch (InvalidOperationException)
      {
        runtime.Dispose();
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.SpawnCellUnavailable,
          $"Spawn cell {placement.Position} for {placement.Loadout.Combatant.Name} is not occupiable."));
      }
    }

    runtime.ExecuteAction(BattleAction.StartBattle());

    return Right<BattleSetupFailure, BattleRuntime>(runtime);
  }

  public static Either<BattleSetupFailure, BattleRuntime> StartFromMap(MapBattleSetup setup)
  {
    ArgumentNullException.ThrowIfNull(setup);
    ArgumentNullException.ThrowIfNull(setup.Map);
    ArgumentNullException.ThrowIfNull(setup.FactionOrder);
    ArgumentNullException.ThrowIfNull(setup.RostersBySlot);
    ArgumentNullException.ThrowIfNull(setup.Objectives);
    if (setup.FactionOrder.Count == 0)
      throw new ArgumentException("FactionOrder must contain at least one faction.", nameof(setup));

    BattleSetupFailure? slotFailure = ValidateSlots(setup);
    if (slotFailure is not null)
      return Left<BattleSetupFailure, BattleRuntime>(slotFailure);

    BattleBoardState board = new(setup.Map);

    var rostersBySlot = new Dictionary<int, IReadOnlyList<Combatant>>();
    var loadoutByCombatant = new Dictionary<Combatant, UnitLoadout>();
    foreach (KeyValuePair<int, IReadOnlyList<UnitLoadout>> entry in setup.RostersBySlot)
    {
      var combatants = new List<Combatant>();
      foreach (UnitLoadout unit in entry.Value)
      {
        combatants.Add(unit.Combatant);
        loadoutByCombatant[unit.Combatant] = unit;
      }

      rostersBySlot[entry.Key] = combatants;
    }

    return MapDeployment.AssignSpawns(setup.Map, rostersBySlot).Match(
      Left: message => Left<BattleSetupFailure, BattleRuntime>(
        new BattleSetupFailure(BattleSetupFailureReason.SpawnSlotShortfall, message)),
      Right: placements =>
      {
        var unitPlacements = new List<UnitPlacement>();
        foreach ((Combatant Combatant, Vector3I Position) placement in placements)
          unitPlacements.Add(new UnitPlacement(loadoutByCombatant[placement.Combatant], placement.Position));

        var coreSetup = new BattleSetup(
          board,
          setup.FactionOrder,
          unitPlacements,
          setup.Objectives,
          setup.Seed,
          setup.PlayerFaction,
          setup.HitChance);
        return Start(coreSetup);
      });
  }

  private static BattleSetupFailure? ValidateSlots(MapBattleSetup setup)
  {
    foreach (KeyValuePair<int, IReadOnlyList<UnitLoadout>> entry in setup.RostersBySlot)
    {
      int slot = entry.Key;
      if (slot < 0 || slot >= setup.FactionOrder.Count)
        return new BattleSetupFailure(
          BattleSetupFailureReason.InvalidSpawnSlot,
          $"Spawn slot {slot} is out of range for {setup.FactionOrder.Count} factions.");

      Faction slotFaction = setup.FactionOrder[slot];
      foreach (UnitLoadout unit in entry.Value)
      {
        if (unit.Combatant.OwningFaction != slotFaction)
          return new BattleSetupFailure(
            BattleSetupFailureReason.SlotFactionMismatch,
            $"Unit {unit.Combatant.Name} in slot {slot} belongs to {unit.Combatant.OwningFaction.Name}, not slot faction {slotFaction.Name}.");
      }
    }

    return null;
  }

  // Spawn-cell occupancy is intentionally NOT pre-validated here: SpawnUnit owns
  // that decision, and the Start spawn loop surfaces its rejection as a typed
  // SpawnCellUnavailable failure. Duplicate cells stay a factory-level check so
  // a shared cell reports the specific DuplicateSpawnCell reason instead of the
  // generic "occupied" rejection SpawnUnit would give for the second unit.
  private static BattleSetupFailure? Validate(BattleSetup setup)
  {
    var factionSet = new SysColGeneric.HashSet<Faction>(setup.FactionOrder);
    return CheckPlayerFaction(setup, factionSet)
      ?? CheckObjectivesPresent(setup)
      ?? CheckObjectiveFactionsKnown(setup, factionSet)
      ?? CheckPlacementFactions(setup, factionSet)
      ?? CheckDuplicateCells(setup);
  }

  private static BattleSetupFailure? CheckPlayerFaction(BattleSetup setup, SysColGeneric.HashSet<Faction> factionSet) =>
    setup.PlayerFaction.Match(
      Some: player => factionSet.Contains(player)
        ? null
        : new BattleSetupFailure(BattleSetupFailureReason.UnknownFaction, $"PlayerFaction {player.Name} is not in FactionOrder."),
      None: () => (BattleSetupFailure?)null);

  private static BattleSetupFailure? CheckObjectivesPresent(BattleSetup setup)
  {
    foreach (Faction faction in setup.FactionOrder)
    {
      if (!setup.Objectives.TryGetValue(faction, out IReadOnlyList<Objective>? objectives) || objectives.Count == 0)
        return new BattleSetupFailure(BattleSetupFailureReason.MissingObjective, $"Faction {faction.Name} has no objective.");
    }

    return null;
  }

  private static BattleSetupFailure? CheckObjectiveFactionsKnown(BattleSetup setup, SysColGeneric.HashSet<Faction> factionSet)
  {
    foreach (Faction faction in setup.Objectives.Keys)
    {
      if (!factionSet.Contains(faction))
        return new BattleSetupFailure(BattleSetupFailureReason.UnknownFaction, $"Objectives reference unknown faction {faction.Name}.");
    }

    return null;
  }

  private static BattleSetupFailure? CheckPlacementFactions(BattleSetup setup, SysColGeneric.HashSet<Faction> factionSet)
  {
    foreach (UnitPlacement placement in setup.Placements)
    {
      Combatant combatant = placement.Loadout.Combatant;
      if (!factionSet.Contains(combatant.OwningFaction))
        return new BattleSetupFailure(
          BattleSetupFailureReason.UnknownFaction,
          $"Unit {combatant.Name} belongs to faction {combatant.OwningFaction.Name}, which is not in FactionOrder.");
    }

    return null;
  }

  private static BattleSetupFailure? CheckDuplicateCells(BattleSetup setup)
  {
    var usedCells = new SysColGeneric.HashSet<Vector3I>();
    foreach (UnitPlacement placement in setup.Placements)
    {
      if (!usedCells.Add(placement.Position))
        return new BattleSetupFailure(
          BattleSetupFailureReason.DuplicateSpawnCell,
          $"Two units share spawn cell {placement.Position}.");
    }

    return null;
  }
}
