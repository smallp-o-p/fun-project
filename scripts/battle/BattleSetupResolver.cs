using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Phase 1 of battle setup: turns authored battle-type data (plus an optional campaign
// deployment) into one concrete BattleSetup — chosen map, ordered sides, resolved seed,
// positioned units and objects. Resolves without executing actions, registering systems,
// or mutating any supplied state; placement checks happen at the startup boundary.
public static class BattleSetupResolver
{
  public static Either<BattleSetupFailure, BattleSetup> Resolve(
    BattleTypeData type,
    int? seed = null,
    Option<PlayerDeployment> playerDeployment = default)
  {
    ArgumentNullException.ThrowIfNull(type);
    if (type.MapPool.Count == 0)
      return Left<BattleSetupFailure, BattleSetup>(new BattleSetupFailure(
        BattleSetupFailureReason.EmptyMapPool, $"{type.Name} has an empty map pool."));
    if (type.PlayerFactionIndex < 0 || type.PlayerFactionIndex >= type.Factions.Count)
      return Left<BattleSetupFailure, BattleSetup>(new BattleSetupFailure(
        BattleSetupFailureReason.UnknownFaction,
        $"PlayerFactionIndex {type.PlayerFactionIndex} is out of range for {type.Factions.Count} factions."));

    int resolvedSeed = seed ?? Random.Shared.Next();
    int chosenIndex = new Random(resolvedSeed).Next(type.MapPool.Count);
    PackedScene chosenScene = type.MapPool[chosenIndex];

    // The pool stores BattleMap-rooted scenes; a throwaway probe instance donates its MapData.
    // `required` is compile-time only, so MapData still needs a runtime null check.
    if (chosenScene is null)
      return Left<BattleSetupFailure, BattleSetup>(new BattleSetupFailure(
        BattleSetupFailureReason.MapSceneInvalid,
        $"{type.Name} map pool entry {chosenIndex} is null."));

    Node probe = chosenScene.Instantiate();
    if (probe is not BattleMap battleMap)
    {
      probe?.Free();
      return Left<BattleSetupFailure, BattleSetup>(new BattleSetupFailure(
        BattleSetupFailureReason.MapSceneInvalid,
        $"{type.Name} map pool entry '{chosenScene.ResourcePath}' roots {probe?.GetType().Name ?? "nothing"}, not a BattleMap."));
    }

    BattleMapData map = battleMap.MapData;
    probe.Free();
    if (map is null)
      return Left<BattleSetupFailure, BattleSetup>(new BattleSetupFailure(
        BattleSetupFailureReason.MapSceneInvalid,
        $"{type.Name} map pool entry '{chosenScene.ResourcePath}' roots a BattleMap without MapData."));

    var sides = new List<BattleSideSetup>();
    for (int slot = 0; slot < type.Factions.Count; slot++)
    {
      FactionDeploymentData authoredSide = type.Factions[slot];
      ArgumentNullException.ThrowIfNull(authoredSide.Faction);
      Faction faction;
      IReadOnlyList<UnitLoadout> loadouts;
      if (slot == type.PlayerFactionIndex && playerDeployment.IsSome)
      {
        PlayerDeployment supplied = playerDeployment.Match(
          Some: value => value,
          None: () => throw new InvalidOperationException("Expected a player deployment."));
        ArgumentNullException.ThrowIfNull(supplied.Faction);
        ArgumentNullException.ThrowIfNull(supplied.Loadouts);
        faction = supplied.Faction;
        loadouts = supplied.Loadouts;
        foreach (UnitLoadout loadout in loadouts)
        {
          ArgumentNullException.ThrowIfNull(loadout);
          ArgumentNullException.ThrowIfNull(loadout.Combatant);
          if (!ReferenceEquals(loadout.Combatant.OwningFaction, faction))
            return Left<BattleSetupFailure, BattleSetup>(new(
              BattleSetupFailureReason.FactionMismatch,
              $"Deployment unit {loadout.Combatant.Name} does not belong to {faction.Name}."));
        }
      }
      else
      {
        faction = new Faction(authoredSide.Faction);
        loadouts = ExpandRoster(authoredSide, faction);
      }

      var assigned = MapDeployment.AssignSpawns(map, slot, loadouts);
      if (assigned.IsLeft)
        return assigned.Match(
          Left: failure => Left<BattleSetupFailure, BattleSetup>(failure),
          Right: _ => throw new InvalidOperationException("Expected a placement failure."));
      IReadOnlyList<UnitPlacement> units = assigned.Match(
        Right: value => value,
        Left: _ => throw new InvalidOperationException("Expected assigned placements."));
      sides.Add(new BattleSideSetup(faction, [.. authoredSide.Objectives], units));
    }

    var objects = new List<ObjectPlacement>();
    foreach (ObjectPlacementData authored in type.Objects)
    {
      // Authored positions are Godot.Vector3I on the exported surface; convert to the
      // runtime Vector3I as they are read off the resource (same boundary as MapDeployment).
      foreach (Godot.Vector3I position in authored.Positions)
        objects.Add(new ObjectPlacement(authored.SpecialObject, new Vector3I(position.X, position.Y, position.Z)));
    }

    return Right<BattleSetupFailure, BattleSetup>(new BattleSetup(
      map, sides, resolvedSeed, Some(sides[type.PlayerFactionIndex].Faction))
    {
      Objects = objects,
      Systems = [.. type.Systems],
      MapScene = Some(chosenScene),
    });
  }

  // Authored quantity/equipment expansion: quantity floors at one; weapons route through
  // ItemRuntimeFactory and armor needs its capability proof. Equipment comes only from the
  // roster entries — campaign equipment slots are never read or written here.
  private static List<UnitLoadout> ExpandRoster(FactionDeploymentData authoredSide, Faction faction)
  {
    var loadouts = new List<UnitLoadout>();
    foreach (RosterEntryData entry in authoredSide.Roster)
    {
      for (int count = 0; count < Math.Max(1, entry.Quantity); count++)
      {
        Combatant combatant = new(entry.Combatant, faction);
        Option<Weapon> weapon = entry.Weapon is null ? None : Some(ItemRuntimeFactory.CreateWeapon(entry.Weapon));
        Option<ItemWith<ArmorCapability>> armor = entry.Armor is null
          ? None
          : ItemRuntimeFactory.Create(entry.Armor).With<ArmorCapability>();
        loadouts.Add(new UnitLoadout(combatant, weapon, armor));
      }
    }

    return loadouts;
  }
}
