#nullable enable
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Weapons;
using Godot;
using System;
using System.Linq;
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
  string? Message = null)
{
  public static BattleMutationResult Success(BattleSessionMutation mutation, BattleUnitState? affectedUnit = null, string? message = null)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    return new BattleMutationResult(mutation, true, BattleMutationFailureReason.None, affectedUnit, message);
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
    int unitId,
    Vector3I destination,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return new MoveUnitStep(unitId, destination, actionPointCost);
  }

  public static MoveUnit MoveUnit(
    int unitId,
    IEnumerable<Vector3I> path,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost
  )
  {
    return new MoveUnit(unitId, path, actionPointCostPerStep);
  }

  public static ThrowItem ThrowItem(int unitId, ThrowableItem item, Vector3I targetCell)
  {
    return new ThrowItem(unitId, item, targetCell);
  }

  public static ApplyDamage ApplyDamage(int unitId, int amount)
  {
    return new ApplyDamage(unitId, amount);
  }

  public static PassUnit PassUnit(int unitId)
  {
    return new PassUnit(unitId);
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

    var unit = session.CreateUnitState(Combatant, Position, EquippedWeapon);
    if (!session.TryAddUnit(unit))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, $"Cannot place a unit at {Position}.");

    session.RaiseEvent(new BattleEvent(BattleEventType.UnitAdded, unit.UnitId, unit.Position));
    return BattleMutationResult.Success(this, unit);
  }
}

public sealed class MoveUnitStep : BattleSessionMutation
{
  public int UnitId { get; }
  public Vector3I Destination { get; }
  public int ActionPointCost { get; }

  public MoveUnitStep(
    int unitId,
    Vector3I destination,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
    : base(MoveUnitStepMutationId)
  {
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    UnitId = unitId;
    Destination = destination;
    ActionPointCost = actionPointCost;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");

    var unit = session.GetLivingUnitOrNull(UnitId);
    if (unit == null || session.ActiveSide == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");
    if (unit.Side != session.ActiveSide)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");
    if (!session.IsUnitStillAvailableThisTurn(UnitId))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");
    if (!session.Board.IsAdjacent(unit.Position, Destination))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");
    if (!session.Board.CanOccupy(Destination))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");
    if (!unit.TrySpendActionPoints(ActionPointCost))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");
    if (!session.Board.TryMoveOccupant(unit.Position, Destination, unit.UnitId))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit step mutation was rejected by the battle session.");

    unit.MoveTo(Destination);
    session.RaiseEvent(new BattleEvent(BattleEventType.UnitMoved, unit.UnitId, Destination));
    return BattleMutationResult.Success(this, unit);
  }
}

public sealed class MoveUnit : BattleSessionMutation
{
  public int UnitId { get; }
  public IReadOnlyList<Vector3I> Path { get; }
  public int ActionPointCostPerStep { get; }

  public MoveUnit(
    int unitId,
    IEnumerable<Vector3I> path,
    int perStepAPCost = BattleSession.DefaultMovementStepActionPointCost)
    : base(MoveUnitMutationId)
  {
    ArgumentNullException.ThrowIfNull(path);
    if (perStepAPCost < 0)
      throw new ArgumentOutOfRangeException(nameof(perStepAPCost), "Action point cost cannot be negative.");

    UnitId = unitId;
    Path = [.. path];
    ActionPointCostPerStep = perStepAPCost;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation was rejected by the battle session.");

    BattleUnitState? unit = session.GetLivingUnitOrNull(UnitId);
    if (unit == null || session.ActiveSide == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation was rejected by the battle session.");
    if (unit.Side != session.ActiveSide)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation was rejected by the battle session.");
    if (!session.IsUnitStillAvailableThisTurn(UnitId))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation was rejected by the battle session.");
    if (Path.Count == 0)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation requires a non-empty path.");
    if (Path[0] != unit.Position)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation path must start at the unit's current position.");

    Vector3I destination = Path[^1];

    int stepCount = Path.Count - 1;
    if (stepCount == 0)
      return BattleMutationResult.Success(this, unit, $"Unit already occupies {destination}.");

    long totalActionPointCost = (long)stepCount * ActionPointCostPerStep;
    if (totalActionPointCost > int.MaxValue)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.UnexpectedError, "Move unit mutation exceeded the supported action point cost.");
    if (unit.CurrentActionPoints < totalActionPointCost)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Move unit mutation was rejected by the battle session.");

    BattleUnitState currentUnit = unit;
    for (int stepIndex = 1; stepIndex < Path.Count; stepIndex++)
    {
      MoveUnitStep stepMutation = new(UnitId, Path[stepIndex], ActionPointCostPerStep);
      BattleMutationResult stepResult = stepMutation.Execute(session);
      if (!stepResult.Succeeded)
        return BattleMutationResult.Failure(this, stepResult.FailureReason, stepResult.Message ?? "Move unit mutation was rejected by the battle session.");

      currentUnit = stepResult.AffectedUnit ?? currentUnit;
    }

    return BattleMutationResult.Success(this, currentUnit, $"Moved to {destination}.");
  }
}

public sealed class ThrowItem : BattleSessionMutation
{
  public int UnitId { get; }
  public ThrowableItem Item { get; }
  public Vector3I TargetCell { get; }

  public ThrowItem(int unitId, ThrowableItem item, Vector3I targetCell)
    : base(ThrowItemMutationId)
  {
    Item = item ?? throw new ArgumentNullException(nameof(item));
    UnitId = unitId;
    TargetCell = targetCell;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");

    var unit = session.GetLivingUnitOrNull(UnitId);
    if (unit == null || session.ActiveSide == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (unit.Side != session.ActiveSide)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (!session.IsUnitStillAvailableThisTurn(UnitId))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (!session.Board.IsInBounds(TargetCell))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (!unit.HasInventoryItem(Item))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (Item.ConsumesOnUse && Item.IsDepleted)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (BattleSession.GetGridDistance(unit.Position, TargetCell) > Item.ThrowRange)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");
    if (!unit.TrySpendActionPoints(Item.ActionPointCost))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");

    if (Item.ConsumesOnUse)
    {
      if (!Item.TrySpendCharge())
        return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Throw item mutation was rejected by the battle session.");

      if (Item.IsDepleted)
        unit.RemoveInventoryItem(Item);
    }

    session.RaiseEvent(new BattleEvent(BattleEventType.ItemThrown, unit.UnitId, TargetCell, $"{unit.Combatant.Name} threw {Item.ItemName}."));
    return BattleMutationResult.Success(this, unit);
  }
}

public sealed class ApplyDamage : BattleSessionMutation
{
  public int UnitId { get; }
  public int Amount { get; }

  public ApplyDamage(int unitId, int amount)
    : base(ApplyDamageMutationId)
  {
    Amount = amount;
    UnitId = unitId;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    var unit = session.GetLivingUnitOrNull(UnitId);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, $"Unknown unit id {UnitId}.");

    unit.ReceiveDamage(Amount);
    session.RaiseEvent(new BattleEvent(BattleEventType.UnitDamaged, UnitId, unit.Position, $"Damage: {Amount}"));

    if (!unit.IsDead)
      return BattleMutationResult.Success(this, unit);

    session.HandleUnitDeath(unit);
    return BattleMutationResult.Success(this, session.GetUnitOrNull(UnitId));
  }
}

public sealed class PassUnit : BattleSessionMutation
{
  public int UnitId { get; }

  public PassUnit(int unitId)
    : base(PassUnitMutationId)
  {
    UnitId = unitId;
  }

  protected override BattleMutationResult ExecuteCore(BattleSession session)
  {
    var activeSide = session.ActiveSide;
    if (session.Phase != BattlePhase.InProgress || activeSide == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Pass unit mutation was rejected by the battle session.");

    var unit = session.GetLivingUnitOrNull(UnitId);
    if (unit == null)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Pass unit mutation was rejected by the battle session.");
    if (unit.Side != activeSide)
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Pass unit mutation was rejected by the battle session.");
    if (!session.TryRemoveAvailableUnit(UnitId))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "Pass unit mutation was rejected by the battle session.");

    session.RaiseEvent(new BattleEvent(BattleEventType.UnitActivationEnded, unit.UnitId, unit.Position, $"{unit.Combatant.Name} ended their activation."));

    if (!session.GetFactionAlive(activeSide).Any(session.CanUnitActNow))
      session.AdvanceTurn();

    return BattleMutationResult.Success(this, session.GetUnitOrNull(UnitId));
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
    if (!session.TryEndFactionTurn(ExpectedActiveSide))
      return BattleMutationResult.Failure(this, BattleMutationFailureReason.Rejected, "End faction turn mutation was rejected by the battle session.");

    return BattleMutationResult.Success(this);
  }
}
