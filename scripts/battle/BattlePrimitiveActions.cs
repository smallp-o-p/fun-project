using FunProject.Combatants;
using FunProject.Items;
using FunProject.Weapons;
using Godot;
using System;

namespace FunProject.Battle;

public sealed class StartBattle : BattleAction
{
  public const string StartBattleActionId = "start_battle";

  public StartBattle()
    : base(StartBattleActionId)
  {
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Setup)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "BattleSession can only be started from setup.");
    if (!session.TryStartBattle())
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Cannot start a battle without at least one living faction in the session.");

    return BattleActionResult.Success(this);
  }
}

public sealed class SpawnUnit : BattleAction
{
  public const string SpawnUnitActionId = "spawn_unit";

  public Combatant Combatant { get; }
  public Vector3I Position { get; }
  public Option<Weapon> EquippedWeapon { get; }

  public SpawnUnit(Combatant combatant, Vector3I position)
    : this(combatant, position, None)
  {
  }

  public SpawnUnit(Combatant combatant, Vector3I position, Weapon equippedWeapon)
    : this(combatant, position, Some(equippedWeapon))
  {
  }

  public SpawnUnit(Combatant combatant, Vector3I position, Option<Weapon> equippedWeapon)
    : base(SpawnUnitActionId)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    Position = position;
    EquippedWeapon = equippedWeapon;
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    if (session.Phase == BattlePhase.Ended)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Cannot add units after the battle has ended.");

    Option<BattleBoardState.ValidatedPoint> positionPointOption = session.Board.ValidatePoint(Position);
    if (positionPointOption.IsNone)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Cannot place a unit at {Position}.");
    BattleBoardState.ValidatedPoint positionPoint = positionPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
    if (!session.Board.CanOccupy(positionPoint))
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Cannot place a unit at {Position}.");

    var spawnedUnit = session.AddUnit(Combatant, positionPoint, EquippedWeapon);
    return BattleActionResult.Success(this, spawnedUnit.Unit);
  }
}

internal sealed class MoveUnitStep : BattleAction
{
  public const string MoveUnitStepActionId = "move_unit_step";

  internal BattleUnitState Unit { get; }
  internal int UnitId => Unit.Id;
  internal BattleBoardState.ValidatedPoint Source { get; }
  internal BattleBoardState.ValidatedPoint Destination { get; }
  internal int ActionPointCost { get; }

  internal MoveUnitStep(
    BattleUnitState unit,
    BattleBoardState.ValidatedPoint source,
    BattleBoardState.ValidatedPoint destination,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
    : base(MoveUnitStepActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCost, 0);

    Unit = unit;
    Source = source;
    Destination = destination;
    ActionPointCost = actionPointCost;
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return ValidateActingUnit(session, Unit, ActionPointCost).Match(
      failure => failure,
      unit =>
      {
        Option<BattleBoardState.ValidatedPoint> sourcePointOption = session.GetUnitPosition(unit);
        if (sourcePointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {UnitId} is not on a valid tile.");
        BattleBoardState.ValidatedPoint sourcePoint = sourcePointOption.IfNone(default(BattleBoardState.ValidatedPoint));

        if (sourcePoint != Source)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {UnitId} is no longer at {Source.Raw}.");
        if (!session.Board.CanOccupy(Destination))
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Destination} cannot be occupied.");
        if (!unit.TrySpendActionPoints(ActionPointCost))
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Move unit step action could not spend {ActionPointCost} action points for unit {UnitId}.");
        if (!session.TryMoveUnit(Unit, Source, Destination))
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Move unit step action could not move unit {UnitId} to {Destination.Raw}.");

        return BattleActionResult.Success(this, unit);
      });
  }
}

public sealed class ThrowItem : BattleAction
{
  public const string ThrowItemActionId = "throw_item";

  public BattleUnitState Unit { get; }
  public ThrowableItem Item { get; }
  public Vector3I TargetCell { get; }

  public ThrowItem(BattleUnitState unit, ThrowableItem item, Vector3I targetCell)
    : base(ThrowItemActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(item);

    Unit = unit;
    Item = item;
    TargetCell = targetCell;
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    return ValidateActingUnit(session, Unit, Item.ActionPointCost).Match(
      failure => failure,
      unit =>
      {
        Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(unit);
        if (unitPointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {unit.Id} is not on a valid tile.");
        Vector3I unitPosition = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint)).Raw;

        Option<BattleBoardState.ValidatedPoint> targetPointOption = session.Board.ValidatePoint(TargetCell);
        if (targetPointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{TargetCell} is outside the battle board.");
        BattleBoardState.ValidatedPoint targetPoint = targetPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
        if (!unit.HasInventoryItem(Item))
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} does not have {Item.ItemName}.");
        if (Item.ConsumesOnUse && Item.IsDepleted)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Item.ItemName} has no charges remaining.");
        if (BattleSession.GetGridDistance(unitPosition, TargetCell) > Item.ThrowRange)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{TargetCell} is out of range for {Item.ItemName}.");
        if (!unit.TrySpendActionPoints(Item.ActionPointCost))
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"{unit.Combatant.Name} could not spend {Item.ActionPointCost} action points.");

        if (Item.ConsumesOnUse)
        {
          if (!Item.TrySpendCharge())
            return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"{Item.ItemName} could not spend a charge.");

          if (Item.IsDepleted)
            unit.RemoveInventoryItem(Item);
        }

        session.RaiseEvent(new ItemThrownBattleEvent(unit, targetPoint, Item));
        return BattleActionResult.Success(this, unit);
      });
  }
}

public sealed class ApplyDamage : BattleAction
{
  public const string ApplyDamageActionId = "apply_damage";

  public BattleUnitState Unit { get; }
  public int Amount { get; }

  public ApplyDamage(BattleUnitState unit, int amount)
    : base(ApplyDamageActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Amount = amount;
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (!Unit.IsAlive)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit {Unit.Id} is not alive.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    if (unitPointOption.IsNone)
      return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Damage action could not resolve position for unit {Unit.Id}.");

    session.DealDamageTo(Unit, Amount);

    return BattleActionResult.Success(this, Unit);
  }
}

public sealed class PassUnit : BattleAction
{
  public const string PassUnitActionId = "pass_unit";

  public BattleUnitState Unit { get; }
  internal int UnitId => Unit.Id;

  public PassUnit(BattleUnitState unit)
    : base(PassUnitActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    return ValidateActingUnit(session, Unit).Match(
      failure => failure,
      unit =>
      {
        session.EndUnitActivation(unit);
        return BattleActionResult.Success(this, unit);
      });
  }
}

public sealed class EndFactionTurn : BattleAction
{
  public const string EndFactionTurnActionId = "end_faction_turn";

  public Faction ExpectedActiveSide { get; }

  public EndFactionTurn(Faction expectedActiveSide)
    : base(EndFactionTurnActionId)
  {
    ArgumentNullException.ThrowIfNull(expectedActiveSide);
    ExpectedActiveSide = expectedActiveSide;
  }

  public override BattleActionResult Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Battle is not in progress.");

    Faction activeSide = session.ActiveSide;
    if (activeSide != ExpectedActiveSide)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{ExpectedActiveSide.Name} cannot end a turn while {activeSide.Name} is active.");

    session.EndFactionTurn(ExpectedActiveSide);
    return BattleActionResult.Success(this);
  }
}
