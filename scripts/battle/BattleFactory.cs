using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// The production "front door": turns caller-supplied inputs into a started, observable
// BattleRuntime. Lives in the assembly so it can drive the internal session/action primitives.
public static class BattleFactory
{
  public static Either<BattleSetupFailure, BattleRuntime> Start(
    BattleTypeData type,
    int? seed = null,
    Option<IReadOnlyList<UnitLoadout>> playerRosterOverride = default)
  {
    ArgumentNullException.ThrowIfNull(type);
    if (type.MapPool.Count == 0)
      return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
        BattleSetupFailureReason.EmptyMapPool, $"{type.Name} has an empty map pool."));
    if (type.PlayerFactionIndex < 0 || type.PlayerFactionIndex >= type.Factions.Count)
      return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
        BattleSetupFailureReason.UnknownFaction,
        $"PlayerFactionIndex {type.PlayerFactionIndex} is out of range for {type.Factions.Count} factions."));

    int resolvedSeed = seed ?? Random.Shared.Next();
    BattleMapData map = type.MapPool[new Random(resolvedSeed).Next(type.MapPool.Count)];

    List<Faction> factionOrder = [];
    Dictionary<Faction, IReadOnlyList<ObjectiveData>> objectives = [];
    foreach (FactionDeploymentData deployment in type.Factions)
    {
      Faction faction = new(deployment.Faction);
      factionOrder.Add(faction);
      objectives[faction] = [.. deployment.Objectives];
    }

    Faction playerFaction = factionOrder[type.PlayerFactionIndex];

    Dictionary<int, IReadOnlyList<Combatant>> rostersBySlot = [];
    Dictionary<Combatant, UnitLoadout> loadoutByCombatant = [];
    for (int slot = 0; slot < type.Factions.Count; slot++)
    {
      Faction slotFaction = factionOrder[slot];
      List<UnitLoadout> loadouts = [];
      bool useOverride = slot == type.PlayerFactionIndex && playerRosterOverride.IsSome;
      if (useOverride)
      {
        IReadOnlyList<UnitLoadout> overrideLoadouts = playerRosterOverride.Match(
          Some: playerLoadouts => playerLoadouts,
          None: () => throw new InvalidOperationException("Player roster override disappeared after IsSome check."));
        foreach (UnitLoadout loadout in overrideLoadouts)
        {
          loadout.Combatant.OwningFaction = slotFaction;
          loadouts.Add(loadout);
        }
      }
      else
      {
        foreach (RosterEntryData entry in type.Factions[slot].Roster)
        {
          for (int count = 0; count < Math.Max(1, entry.Quantity); count++)
          {
            Combatant combatant = new(entry.Combatant, slotFaction);
            Option<Weapon> weapon = entry.Weapon is null ? None : Some(ItemRuntimeFactory.CreateWeapon(entry.Weapon));
            Option<ItemWith<ArmorCapability>> armor = entry.Armor is null
              ? None
              : ItemRuntimeFactory.Create(entry.Armor).With<ArmorCapability>();
            loadouts.Add(new UnitLoadout(combatant, weapon, armor));
          }
        }
      }

      List<Combatant> combatants = [];
      foreach (UnitLoadout loadout in loadouts)
      {
        combatants.Add(loadout.Combatant);
        loadoutByCombatant[loadout.Combatant] = loadout;
      }

      rostersBySlot[slot] = combatants;
    }

    return MapDeployment.AssignSpawns(map, rostersBySlot).Match(
      Left: message => Left<BattleSetupFailure, BattleRuntime>(
        new BattleSetupFailure(BattleSetupFailureReason.SpawnSlotShortfall, message)),
      Right: spawns =>
      {
        List<UnitPlacement> unitPlacements = [];
        foreach ((Combatant Combatant, Vector3I Position) spawn in spawns)
          unitPlacements.Add(new UnitPlacement(loadoutByCombatant[spawn.Combatant], spawn.Position));

        List<ObjectPlacement> objectPlacements = [];
        foreach (ObjectPlacementData authored in type.Objects)
        {
          // Authored positions are Godot.Vector3I on the exported surface; convert to the
          // runtime Vector3I as they are read off the resource (same boundary as MapDeployment).
          foreach (var position in authored.Positions)
            objectPlacements.Add(new ObjectPlacement(authored.SpecialObject, new Vector3I(position.X, position.Y, position.Z)));
        }

        BattleSetup setup = new(
          Board: new BattleBoardState(map),
          FactionOrder: factionOrder,
          Placements: unitPlacements,
          Objectives: objectives,
          Seed: resolvedSeed,
          PlayerFaction: Some(playerFaction),
          Objects: objectPlacements);

        return Start(setup).Match(
          Right: runtime =>
          {
            foreach (BattleTypeSystemData declared in type.Systems)
              declared.Register(runtime);

            return Right<BattleSetupFailure, BattleRuntime>(runtime);
          },
          Left: failure => Left<BattleSetupFailure, BattleRuntime>(failure));
      });
  }

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

    foreach (KeyValuePair<Faction, IReadOnlyList<ObjectiveData>> entry in setup.Objectives)
    {
      foreach (ObjectiveData data in entry.Value)
        session.AddObjective(entry.Key, data.Instantiate());
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
        placement.Loadout.Armor,
        placement.Loadout.StatMods);
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

    var objectCells = new SysColGeneric.HashSet<Vector3I>();
    foreach (ObjectPlacement placement in setup.Objects ?? [])
    {
      if (!objectCells.Add(placement.Position))
      {
        runtime.Dispose();
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.DuplicateObjectCell,
          $"Two objects share cell {placement.Position}."));
      }

      Option<BattleBoardState.ValidatedPoint> objectPoint = setup.Board.ValidatePoint(placement.Position);
      if (objectPoint.IsNone)
      {
        runtime.Dispose();
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.ObjectCellUnavailable,
          $"Object cell {placement.Position} for {placement.Data.Name} is out of bounds."));
      }

      try
      {
        runtime.ExecuteAction(BattleAction.PlaceObject(placement.Data, objectPoint.Value()));
      }
      catch (InvalidOperationException)
      {
        runtime.Dispose();
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.ObjectCellUnavailable,
          $"Object cell {placement.Position} for {placement.Data.Name} is not occupiable."));
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
      if (!setup.Objectives.TryGetValue(faction, out IReadOnlyList<ObjectiveData>? objectives) || objectives.Count == 0)
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
