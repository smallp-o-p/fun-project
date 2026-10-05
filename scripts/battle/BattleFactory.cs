using FunProject.Combatants;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// The production "front door": turns authored battle types or concrete setups into a started,
// observable BattleRuntime. Lives in the assembly so it can drive the internal session/action
// primitives. Start(BattleTypeData, ...) composes resolution with the single startup routine;
// Start(BattleSetup, ...) is that routine.
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

    // Parse the entire layout before any objective instantiation or runtime construction:
    // known layout failures return typed results before systems register or units spawn.
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

    // Materialize the initial roster/objects on the factory-owned board with the existing ID
    // conventions (units nonnegative in setup order, objects negative), then instantiate the
    // objectives. A placement miss after parsing is a trusted-invariant error, not a typed
    // setup failure. The same unit constructor reinforcement uses; no buff/stat/inventory
    // duplication.
    var runtimeUnits = new List<BattleUnitState>(units.Count);
    var placementEvents = new List<BattleEvent>(units.Count + objects.Count);
    foreach (var (loadout, point) in units)
    {
      var unit = new BattleUnitState(
        runtimeUnits.Count, loadout.Combatant, loadout.Weapon, loadout.Armor, loadout.StatMods);
      if (!board.TryPlaceOccupant(point, unit.Id))
        throw new InvalidOperationException($"Could not place unit {unit.Id} at {point.Raw}.");
      runtimeUnits.Add(unit);
      placementEvents.Add(new UnitAddedBattleEvent(unit, point));
    }

    var runtimeObjects = new List<BattleObjectState>(objects.Count);
    foreach (var (data, point) in objects)
    {
      var state = new BattleObjectState(-runtimeObjects.Count - 1, data, point.Raw);
      if (!board.TryPlaceObjectOccupant(point, state.Id))
        throw new InvalidOperationException($"Object cell {point.Raw} is not occupiable.");
      runtimeObjects.Add(state);
      placementEvents.Add(new ObjectPlacedBattleEvent(state, point));
    }

    var objectives = new List<(Faction, Objective)>(setup.Sides.Count);
    foreach (BattleSideSetup side in setup.Sides)
      foreach (ObjectiveData objective in side.Objectives)
        objectives.Add((side.Faction, objective.Instantiate()));

    var session = new BattleSession(board,
      setup.Sides.AsValueEnumerable().Select(side => side.Faction).ToArray(),
      runtimeUnits, runtimeObjects, objectives, hitChance, setup.Seed, setup.PlayerFaction);
    var runtime = new BattleRuntime(session);
    try
    {
      var hooks = new BattleHookRegistration(runtime);
      foreach (BattleTypeSystemData system in setup.Systems)
        system.Register(hooks);
      runtime.ExecuteAction(new BootstrapBattle(placementEvents));
      return Right<BattleSetupFailure, BattleRuntime>(runtime);
    }
    catch
    {
      // Arbitrary hook/objective/action faults dispose the runtime and propagate unchanged;
      // they are not translated into typed cell failures, and nothing is rolled back.
      runtime.Dispose();
      throw;
    }
  }

  // Factory-owned startup notification, invoked exactly once per start: it announces every
  // initial placement through one executor action window (so hooks keep a valid source
  // action and interrupts work through the existing machinery), then session start, then the
  // first turn. All placements are enqueued before dispatch, so the first visibility refresh
  // appends its deterministic spottings after the placement announcements, and hook-born
  // follow-ups keep the normal queue order. An outcome decided mid-dispatch finishes the
  // remaining notifications but skips the later stages; the executor drops queued interrupts
  // once completion exists.
  private sealed class BootstrapBattle(IReadOnlyList<BattleEvent> placementEvents) : BattleAction
  {
    public override Result Execute(BattleSession session)
    {
      session.RaiseEvents([.. placementEvents]);
      if (session.Outcome.IsSome)
        return Result.Completed;

      session.RaiseEvents(new SessionStartedBattleEvent());
      if (session.Outcome.IsSome)
        return Result.Completed;

      session.BeginFirstTurn();
      return Result.Completed;
    }
  }
}
