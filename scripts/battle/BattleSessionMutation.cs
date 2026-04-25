#nullable enable
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public enum BattleMutationFailureReason
{
  None,
  Rejected,
  UnsupportedMutation,
  UnexpectedError,
}

public readonly record struct BattleMutationResult(
  BattleSessionMutation Mutation,
  bool Succeeded,
  BattleMutationFailureReason FailureReason = BattleMutationFailureReason.None,
  BattleUnitState? AffectedUnit = null,
  string? Message = null,
  BattleSession.BattleUnitHandle? AffectedUnitHandle = null)
{
  public static BattleMutationResult Success(
    BattleSessionMutation mutation,
    BattleUnitState? affectedUnit = null,
    string? message = null,
    BattleSession.BattleUnitHandle? affectedUnitHandle = null)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    return new BattleMutationResult(mutation, true, BattleMutationFailureReason.None, affectedUnit, message, affectedUnitHandle);
  }

  public static BattleMutationResult Failure(BattleSessionMutation mutation, BattleMutationFailureReason failureReason, string? message = null)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    if (failureReason == BattleMutationFailureReason.None)
      throw new ArgumentOutOfRangeException(nameof(failureReason), "Failed mutations must specify a failure reason.");

    return new BattleMutationResult(mutation, false, failureReason, null, message);
  }
}

public abstract class BattleSessionMutation
{
  public const string StartBattleMutationId = "start_battle";
  public const string SpawnUnitMutationId = "spawn_unit";
  public const string MoveUnitStepMutationId = "move_unit_step";
  public const string MoveUnitMutationId = "move_unit";
  public const string ThrowItemMutationId = "throw_item";
  public const string ApplyDamageMutationId = "apply_damage";
  public const string PassUnitMutationId = "pass_unit";
  public const string EndFactionTurnMutationId = "end_faction_turn";

  public string MutationId { get; }

  protected BattleSessionMutation(string mutationId)
  {
    if (string.IsNullOrWhiteSpace(mutationId))
      throw new ArgumentException("Mutation id cannot be null or whitespace.", nameof(mutationId));

    MutationId = mutationId;
  }

  public BattleMutationResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return new BattleActionExecutor(session).ExecuteNow(this);
  }

  internal BattleMutationResult ExecuteUnchecked(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    var result = ExecuteCore(session);
    if (result.Succeeded)
      session.RefreshVisibility();

    return result;
  }

  protected abstract BattleMutationResult ExecuteCore(BattleSession session);

  public static StartBattle StartBattle()
  {
    return new StartBattle();
  }

  public static SpawnUnit SpawnUnit(Combatant combatant, Vector3I position, Weapon? equippedWeapon = null)
  {
    return new SpawnUnit(combatant, position, equippedWeapon);
  }

  public static MoveUnitStep MoveUnitStep(
    BattleSession.BattleUnitHandle unitHandle,
    Vector3I destination,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return new MoveUnitStep(unitHandle, destination, actionPointCost);
  }

  public static MoveUnit MoveUnit(
    BattleSession.BattleUnitHandle unitHandle,
    IEnumerable<Vector3I> path,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost
  )
  {
    return new MoveUnit(unitHandle, path, actionPointCostPerStep);
  }

  public static ThrowItem ThrowItem(BattleSession.BattleUnitHandle unitHandle, ThrowableItem item, Vector3I targetCell)
  {
    return new ThrowItem(unitHandle, item, targetCell);
  }

  public static ApplyDamage ApplyDamage(BattleSession.BattleUnitHandle unitHandle, int amount)
  {
    return new ApplyDamage(unitHandle, amount);
  }

  public static PassUnit PassUnit(BattleSession.BattleUnitHandle unitHandle)
  {
    return new PassUnit(unitHandle);
  }

  public static EndFactionTurn EndFactionTurn(Faction expectedActiveSide)
  {
    return new EndFactionTurn(expectedActiveSide);
  }
}

public sealed class StartBattle : BattleSessionMutation
{
  public StartBattle()
    : base(StartBattleMutationId)
  {
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    if (session.Phase != BattlePhase.Setup)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "BattleSession can only be started from setup.");
    if (!session.TryStartBattle())
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Cannot start a battle without at least one living faction in the session.");

    return BattleMutationResult.Success(this);
  }
}

public sealed class SpawnUnit : BattleSessionMutation
{
  public Combatant Combatant { get; }
  public Vector3I Position { get; }
  public Weapon? EquippedWeapon { get; }

  public SpawnUnit(Combatant combatant, Vector3I position, Weapon? equippedWeapon = null)
    : base(SpawnUnitMutationId)
  {
    Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));
    Position = position;
    EquippedWeapon = equippedWeapon;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    if (session.Phase == BattlePhase.Ended)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Cannot add units after the battle has ended.");
    if (!session.Board.CanOccupy(Position))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, $"Cannot place a unit at {Position}.");

    var spawnedUnit = session.AddUnit(Combatant, Position, EquippedWeapon);
    session.RaiseEvent(new BattleEvent(BattleEventType.UnitAdded, spawnedUnit.Unit.UnitId, spawnedUnit.Unit.Position));
    return BattleMutationResult.Success(this, spawnedUnit.Unit, affectedUnitHandle: spawnedUnit.Handle);
  }
}

public sealed class MoveUnitStep : BattleSessionMutation
{
  public BattleSession.BattleUnitHandle UnitHandle { get; }
  internal int UnitId => UnitHandle.UnitId;
  public Vector3I Destination { get; }
  public int ActionPointCost { get; }

  public MoveUnitStep(
    BattleSession.BattleUnitHandle unitHandle,
    Vector3I destination,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
    : base(MoveUnitStepMutationId)
  {
    UnitHandle = unitHandle ?? throw new ArgumentNullException(nameof(unitHandle));
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    Destination = destination;
    ActionPointCost = actionPointCost;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    var unit = session.GetLivingUnitOrNull(UnitHandle);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Move unit step mutation could not resolve unit {UnitId}.");
    if (!unit.TrySpendActionPoints(ActionPointCost))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Move unit step mutation could not spend {ActionPointCost} action points for unit {UnitId}.");
    if (!session.Board.TryMoveOccupant(unit.Position, Destination, unit.UnitId))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Move unit step mutation could not move unit {UnitId} to {Destination}.");

    unit.MoveTo(Destination);
    session.RaiseEvent(new BattleEvent(BattleEventType.UnitMoved, unit.UnitId, Destination));
    return BattleMutationResult.Success(this, unit, affectedUnitHandle: UnitHandle);
  }
}

public sealed class MoveUnit : BattleSessionMutation
{
  public BattleSession.BattleUnitHandle UnitHandle { get; }
  internal int UnitId => UnitHandle.UnitId;
  public IReadOnlyList<Vector3I> Path { get; }
  public int ActionPointCostPerStep { get; }

  public MoveUnit(
    BattleSession.BattleUnitHandle unitHandle,
    IEnumerable<Vector3I> path,
    int perStepAPCost = BattleSession.DefaultMovementStepActionPointCost)
    : base(MoveUnitMutationId)
  {
    UnitHandle = unitHandle ?? throw new ArgumentNullException(nameof(unitHandle));
    ArgumentNullException.ThrowIfNull(path);
    if (perStepAPCost < 0)
      throw new ArgumentOutOfRangeException(nameof(perStepAPCost), "Action point cost cannot be negative.");

    Path = [.. path];
    ActionPointCostPerStep = perStepAPCost;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    BattleUnitState? unit = session.GetLivingUnitOrNull(UnitHandle);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Move unit mutation could not resolve unit {UnitId}.");
    if (Path.Count == 0)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, "Move unit mutation requires a non-empty path.");
    if (Path[0] != unit.Position)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, "Move unit mutation path must start at the unit's current position.");

    Vector3I destination = Path[^1];

    int stepCount = Path.Count - 1;
    if (stepCount == 0)
      return BattleMutationResult.Success(this, unit, $"Unit already occupies {destination}.", UnitHandle);
    long totalActionPointCost = (long)stepCount * ActionPointCostPerStep;
    if (unit.CurrentActionPoints < totalActionPointCost)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"{unit.Combatant.Name} needs {totalActionPointCost} action points but only has {unit.CurrentActionPoints}.");

    for (int stepIndex = 1; stepIndex < Path.Count; stepIndex++)
    {
      Vector3I previousStep = Path[stepIndex - 1];
      Vector3I step = Path[stepIndex];
      if (!session.Board.IsAdjacent(previousStep, step))
        return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Move unit path step {step} is not adjacent to {previousStep}.");
      if (!session.Board.CanOccupy(step))
        return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Move unit path step {step} cannot be occupied.");
    }

    BattleUnitState currentUnit = unit;
    for (int stepIndex = 1; stepIndex < Path.Count; stepIndex++)
    {
      MoveUnitStep stepMutation = new(UnitHandle, Path[stepIndex], ActionPointCostPerStep);
      BattleMutationResult stepResult = stepMutation.ExecuteUnchecked(session);
      if (!stepResult.Succeeded)
        return BattleMutationResult.Failure(this, stepResult.FailureReason, stepResult.Message ?? "Move unit mutation could not be applied.");

      currentUnit = stepResult.AffectedUnit ?? currentUnit;
    }

    return BattleMutationResult.Success(this, currentUnit, $"Moved to {destination}.", UnitHandle);
  }
}

public sealed class ThrowItem : BattleSessionMutation
{
  public BattleSession.BattleUnitHandle UnitHandle { get; }
  internal int UnitId => UnitHandle.UnitId;
  public ThrowableItem Item { get; }
  public Vector3I TargetCell { get; }

  public ThrowItem(BattleSession.BattleUnitHandle unitHandle, ThrowableItem item, Vector3I targetCell)
    : base(ThrowItemMutationId)
  {
    UnitHandle = unitHandle ?? throw new ArgumentNullException(nameof(unitHandle));
    Item = item ?? throw new ArgumentNullException(nameof(item));
    TargetCell = targetCell;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    var unit = session.GetLivingUnitOrNull(UnitHandle);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Throw item mutation could not resolve unit {UnitId}.");
    if (!unit.HasInventoryItem(Item))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"{unit.Combatant.Name} no longer has {Item.ItemName}.");
    if (Item.ConsumesOnUse && Item.IsDepleted)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"{Item.ItemName} has no charges remaining.");
    if (!unit.TrySpendActionPoints(Item.ActionPointCost))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"{unit.Combatant.Name} could not spend {Item.ActionPointCost} action points.");

    if (Item.ConsumesOnUse)
    {
      if (!Item.TrySpendCharge())
        return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"{Item.ItemName} could not spend a charge.");

      if (Item.IsDepleted)
        unit.RemoveInventoryItem(Item);
    }

    session.RaiseEvent(new BattleEvent(BattleEventType.ItemThrown, unit.UnitId, TargetCell, $"{unit.Combatant.Name} threw {Item.ItemName}."));
    return BattleMutationResult.Success(this, unit, affectedUnitHandle: UnitHandle);
  }
}

public sealed class ApplyDamage : BattleSessionMutation
{
  public BattleSession.BattleUnitHandle UnitHandle { get; }
  internal int UnitId => UnitHandle.UnitId;
  public int Amount { get; }

  public ApplyDamage(BattleSession.BattleUnitHandle unitHandle, int amount)
    : base(ApplyDamageMutationId)
  {
    UnitHandle = unitHandle ?? throw new ArgumentNullException(nameof(unitHandle));
    Amount = amount;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    var unit = session.GetLivingUnitOrNull(UnitHandle);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, $"Unknown unit id {UnitId}.");

    unit.ReceiveDamage(Amount);
    session.RaiseEvent(new BattleEvent(BattleEventType.UnitDamaged, UnitId, unit.Position, $"Damage: {Amount}"));

    if (!unit.IsDead)
      return BattleMutationResult.Success(this, unit, affectedUnitHandle: UnitHandle);

    session.HandleUnitDeath(unit);
    return BattleMutationResult.Success(this, session.GetUnitOrNull(UnitHandle), affectedUnitHandle: UnitHandle);
  }
}

public sealed class PassUnit : BattleSessionMutation
{
  public BattleSession.BattleUnitHandle UnitHandle { get; }
  internal int UnitId => UnitHandle.UnitId;

  public PassUnit(BattleSession.BattleUnitHandle unitHandle)
    : base(PassUnitMutationId)
  {
    UnitHandle = unitHandle ?? throw new ArgumentNullException(nameof(unitHandle));
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    var unit = session.GetLivingUnitOrNull(UnitHandle);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, $"Pass unit mutation could not resolve unit {UnitId}.");

    session.EndUnitActivation(unit);
    return BattleMutationResult.Success(this, unit, affectedUnitHandle: UnitHandle);
  }
}

public sealed class EndFactionTurn : BattleSessionMutation
{
  public Faction ExpectedActiveSide { get; }

  public EndFactionTurn(Faction expectedActiveSide)
    : base(EndFactionTurnMutationId)
  {
    ExpectedActiveSide = expectedActiveSide ?? throw new ArgumentNullException(nameof(expectedActiveSide));
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    session.EndFactionTurn(ExpectedActiveSide);
    return BattleMutationResult.Success(this);
  }
}
