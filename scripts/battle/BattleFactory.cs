using FunProject.Combatants;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// The production "front door": turns authored battle types or concrete setups into a started,
// observable BattleRuntime. Lives in the assembly so it can drive the internal preparation
// and runtime doors. Start(BattleTypeData, ...) composes resolution with the single startup
// routine; Start(BattleSetup, ...) is that routine: parse the whole layout, prepare, then
// construct the runtime, register declared systems, and dispatch the opening turn.
public static class BattleFactory
{
  public static Either<BattleSetupFailure, BattleRuntime> Start(
    BattleTypeData type,
    int? seed = null,
    Option<PlayerDeployment> playerDeployment = default)
    => BattleSetupResolver.Resolve(type, seed, playerDeployment)
      .Bind(setup => Start(setup));

  public static Either<BattleSetupFailure, BattleRuntime> Start(
    BattleSetup setup, IHitChanceCalculator? hitChance = null)
  {
    ArgumentNullException.ThrowIfNull(setup);
    ArgumentNullException.ThrowIfNull(setup.Map);
    ArgumentNullException.ThrowIfNull(setup.Sides);
    ArgumentNullException.ThrowIfNull(setup.Objects);
    ArgumentNullException.ThrowIfNull(setup.Systems);
    if (setup.Sides.Count == 0)
      throw new ArgumentException("Sides must contain at least one side.", nameof(setup));

    var sideFactions = new SysColGeneric.HashSet<Faction>();
    foreach (BattleSideSetup side in setup.Sides)
    {
      ArgumentNullException.ThrowIfNull(side);
      ArgumentNullException.ThrowIfNull(side.Faction);
      ArgumentNullException.ThrowIfNull(side.Objectives);
      ArgumentNullException.ThrowIfNull(side.Units);
      if (!sideFactions.Add(side.Faction))
        throw new ArgumentException(
          $"Two sides share the faction reference {side.Faction.Name}.", nameof(setup));
    }

    BattleSetupFailure? playerFailure = setup.PlayerFaction.Match(
      Some: player => sideFactions.Contains(player)
        ? null
        : new BattleSetupFailure(
          BattleSetupFailureReason.UnknownFaction,
          $"PlayerFaction {player.Name} is not among Sides."),
      None: () => null);
    if (playerFailure is not null)
      return Left<BattleSetupFailure, BattleRuntime>(playerFailure);

    // Every later step builds on a board this call owns: failed startups abandon it with the
    // runtime instead of handing partial placements back to the caller.
    BattleBoardState board = new(setup.Map);

    // Parse the entire layout before any objective instantiation or placement: known layout
    // failures return typed results before systems register or units spawn. An empty side is
    // a parse-time rejection — no side may enter preparation without units to field.
    List<(UnitLoadout Loadout, BattleBoardState.ValidatedPoint Point)> units = [];
    List<(BattleSpecialObjectData Data, BattleBoardState.ValidatedPoint Point)> objects = [];
    SysColGeneric.HashSet<Vector3I> unitCells = [];
    SysColGeneric.HashSet<Vector3I> objectCells = [];

    foreach (BattleSideSetup side in setup.Sides)
    {
      if (side.Objectives.Count == 0)
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.MissingObjective,
          $"Faction {side.Faction.Name} has no objective."));
      if (side.Units.Count == 0)
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.NoConsciousUnits,
          $"Faction {side.Faction.Name} has no living, conscious units."));

      foreach (UnitPlacement placement in side.Units)
      {
        ArgumentNullException.ThrowIfNull(placement.Loadout);
        ArgumentNullException.ThrowIfNull(placement.Loadout.Combatant);
        if (!ReferenceEquals(placement.Loadout.Combatant.OwningFaction, side.Faction))
          return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
            BattleSetupFailureReason.FactionMismatch,
            $"Unit {placement.Loadout.Combatant.Name} does not belong to side faction {side.Faction.Name}."));
        if (!unitCells.Add(placement.Position))
          return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
            BattleSetupFailureReason.DuplicateSpawnCell,
            $"Two units share spawn cell {placement.Position}."));

        Option<BattleBoardState.ValidatedPoint> point = board.ValidatePoint(placement.Position);
        if (point.IsNone || !board.CanOccupy(point.Value()))
          return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
            BattleSetupFailureReason.SpawnCellUnavailable,
            $"Spawn cell {placement.Position} for {placement.Loadout.Combatant.Name} is not a valid initial spawn cell."));
        units.Add((placement.Loadout, point.Value()));
      }
    }

    foreach (ObjectPlacement placement in setup.Objects)
    {
      ArgumentNullException.ThrowIfNull(placement.Data);
      if (!objectCells.Add(placement.Position))
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.DuplicateObjectCell,
          $"Two objects share cell {placement.Position}."));
      if (unitCells.Contains(placement.Position))
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.ObjectCellUnavailable,
          $"Object cell {placement.Position} for {placement.Data.Name} overlaps a unit spawn cell."));

      Option<BattleBoardState.ValidatedPoint> point = board.ValidatePoint(placement.Position);
      if (point.IsNone || !board.CanOccupy(point.Value()))
        return Left<BattleSetupFailure, BattleRuntime>(new BattleSetupFailure(
          BattleSetupFailureReason.ObjectCellUnavailable,
          $"Object cell {placement.Position} for {placement.Data.Name} is not a valid initial placement cell."));
      objects.Add((placement.Data, point.Value()));
    }

    // Preparation owns objective instantiation, placement, and the spawn bookkeeping; no
    // runtime exists until it completes, so a failure leaves nothing to dispose.
    var preparation = new BattlePreparation(board,
      setup.Sides.AsValueEnumerable().Select(side => side.Faction).ToArray(),
      hitChance, setup.Seed, setup.PlayerFaction);
    foreach (BattleSideSetup side in setup.Sides)
      foreach (ObjectiveData objective in side.Objectives)
        preparation.AddObjective(side.Faction, objective.Instantiate());
    foreach (var (loadout, point) in units)
      preparation.AddUnit(loadout.Combatant, point, loadout.Weapon, loadout.Armor, loadout.StatMods);
    foreach (var (data, point) in objects)
      preparation.AddObject(data, point);

    return preparation.Complete().Bind(state =>
    {
      var runtime = BattleRuntime.Create(state);
      try
      {
        // Declared systems register only after complete placement and preparation, so they
        // observe session start and the opening turn — never initial placement.
        foreach (BattleTypeSystemData system in setup.Systems)
          system.Register(runtime);
        runtime.DispatchOpeningTurn();
        return Right<BattleSetupFailure, BattleRuntime>(runtime);
      }
      catch
      {
        // Arbitrary hook/objective/action faults dispose the runtime and propagate unchanged;
        // they are not translated into typed cell failures, and nothing is rolled back.
        runtime.Dispose();
        throw;
      }
    });
  }
}
