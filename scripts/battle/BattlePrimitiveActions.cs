using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Linq;

namespace FunProject.Battle;

public sealed class StartBattle : BattleAction
{
  public const string StartBattleActionId = "start_battle";

  internal StartBattle()
    : base(StartBattleActionId)
  {
  }

  internal override BattleActionResult Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Setup)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "BattleSession can only be started from setup.");
    if (!session.AliveUnits.Any())
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Cannot start a battle without at least one living faction in the session.");

    session.StartBattle();
    return BattleActionResult.Success(this);
  }
}

public sealed class SpawnUnit : BattleAction
{
  public const string SpawnUnitActionId = "spawn_unit";

  public Combatant Combatant { get; }
  public Vector3I Position { get; }
  public Option<Weapon> EquippedWeapon { get; }
  public Option<ItemWith<ArmorCapability>> EquippedArmor { get; }

  internal SpawnUnit(Combatant combatant, Vector3I position)
    : this(combatant, position, None, None)
  {
  }

  internal SpawnUnit(Combatant combatant, Vector3I position, Weapon equippedWeapon)
    : this(combatant, position, Some(equippedWeapon), None)
  {
  }

  internal SpawnUnit(
    Combatant combatant,
    Vector3I position,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor)
    : base(SpawnUnitActionId)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    Position = position;
    EquippedWeapon = equippedWeapon;
    EquippedArmor = equippedArmor;
  }

  internal override BattleActionResult Execute(BattleSession session)
  {
    if (session.Phase == BattlePhase.Ended)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Cannot add units after the battle has ended.");

    Option<BattleBoardState.ValidatedPoint> positionPointOption = session.Board.ValidatePoint(Position);
    if (positionPointOption.IsNone)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Cannot place a unit at {Position}.");
    BattleBoardState.ValidatedPoint positionPoint = positionPointOption.Value();
    if (!session.Board.CanOccupy(positionPoint))
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Cannot place a unit at {Position}.");

    var spawnedUnit = session.AddUnit(Combatant, positionPoint, EquippedWeapon, EquippedArmor);
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

  internal override BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return ValidateActingUnit(session, Unit, ActionPointCost).Match(
      failure => failure,
      unit =>
      {
        Option<BattleBoardState.ValidatedPoint> sourcePointOption = session.GetUnitPosition(unit);
        if (sourcePointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {UnitId} is not on a valid tile.");
        BattleBoardState.ValidatedPoint sourcePoint = sourcePointOption.Value();

        if (sourcePoint != Source)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {UnitId} is no longer at {Source.Raw}.");
        if (!session.Board.CanOccupy(Destination))
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Destination} cannot be occupied.");
        if (!unit.TrySpendActionPoints(ActionPointCost))
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Move unit step action could not spend {ActionPointCost} action points for unit {UnitId}.");
        session.MoveUnit(Unit, Source, Destination);

        return BattleActionResult.Success(this, unit);
      });
  }
}

public sealed class ThrowItem : BattleAction
{
  public const string ThrowItemActionId = "throw_item";

  public BattleUnitState Unit { get; }
  public ItemWith<ThrowableCapability> Throwable { get; }
  public Vector3I TargetCell { get; }

  public EquippableItem Item => Throwable.Item;

  internal ThrowItem(BattleUnitState unit, ItemWith<ThrowableCapability> throwable, Vector3I targetCell)
    : base(ThrowItemActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(throwable.Item); // guards default-constructed proof structs
    ArgumentNullException.ThrowIfNull(throwable.Capability);

    Unit = unit;
    Throwable = throwable;
    TargetCell = targetCell;
  }

  internal override BattleActionResult Execute(BattleSession session)
  {
    return ValidateActingUnit(session, Unit, Throwable.Capability.ActionPointCost).Match(
      failure => failure,
      unit =>
      {
        Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(unit);
        if (unitPointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {unit.Id} is not on a valid tile.");
        Vector3I unitPosition = unitPointOption.Value().Raw;

        Option<BattleBoardState.ValidatedPoint> targetPointOption = session.Board.ValidatePoint(TargetCell);
        if (targetPointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{TargetCell} is outside the battle board.");
        BattleBoardState.ValidatedPoint targetPoint = targetPointOption.Value();
        if (!unit.HasInventoryItem(Item))
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} does not have {Item.ItemName}.");

        Option<ChargesCapability> charges = Item.FindCapability<ChargesCapability>();
        if (Throwable.Capability.ConsumesOnUse && charges.Match(c => c.IsDepleted, () => false))
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Item.ItemName} has no charges remaining.");
        if (BattleSession.GetGridDistance(unitPosition, TargetCell) > Throwable.Capability.ThrowRange)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{TargetCell} is out of range for {Item.ItemName}.");
        if (!unit.TrySpendActionPoints(Throwable.Capability.ActionPointCost))
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"{unit.Combatant.Name} could not spend {Throwable.Capability.ActionPointCost} action points.");

        if (Throwable.Capability.ConsumesOnUse)
        {
          charges.Match(
            chargeState =>
            {
              if (!chargeState.TrySpend())
                throw new InvalidOperationException($"{Item.ItemName} could not spend a charge after passing the depletion check.");

              if (chargeState.IsDepleted)
                unit.RemoveInventoryItem(Item);
            },
            // No charges capability: consumable items are implicitly single-use.
            () => unit.RemoveInventoryItem(Item));
        }

        session.RaiseEvent(new ItemThrownBattleEvent(unit, targetPoint, Item));
        return BattleActionResult.Success(this, unit);
      });
  }
}

public sealed class AttackUnit : BattleAction
{
  public const string AttackUnitActionId = "attack_unit";

  public BattleUnitState Unit { get; }
  public BattleUnitState Target { get; }

  internal AttackUnit(BattleUnitState unit, BattleUnitState target)
    : base(AttackUnitActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(target);
    Unit = unit;
    Target = target;
  }

  internal override BattleActionResult Execute(BattleSession session)
  {
    return ValidateActingUnit(session, Unit, BattleSession.DefaultAttackActionPointCost).Match(
      failure => failure,
      attacker =>
      {
        if (attacker.EquippedWeapon.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{attacker.Combatant.Name} has no equipped weapon.");
        Weapon weapon = attacker.RequireEquippedWeapon();

        if (Target == attacker)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{attacker.Combatant.Name} cannot attack itself.");
        if (Target.Side == attacker.Side)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{attacker.Combatant.Name} cannot attack allied unit {Target.Combatant.Name}.");
        if (!Target.IsAlive)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Target.Combatant.Name} is not alive.");
        if (!attacker.VisibleUnits.Contains(Target))
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{attacker.Combatant.Name} cannot see {Target.Combatant.Name}.");

        Option<BattleBoardState.ValidatedPoint> attackerPointOption = session.GetUnitPosition(attacker);
        if (attackerPointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Attack action could not resolve position for unit {attacker.Id}.");
        Option<BattleBoardState.ValidatedPoint> targetPointOption = session.GetUnitPosition(Target);
        if (targetPointOption.IsNone)
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Attack action could not resolve position for unit {Target.Id}.");
        BattleBoardState.ValidatedPoint attackerPoint = attackerPointOption.Value();
        BattleBoardState.ValidatedPoint targetPoint = targetPointOption.Value();

        if (BattleSession.GetGridDistance(attackerPoint.Raw, targetPoint.Raw) > weapon.EffectiveRange)
          return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Target.Combatant.Name} is out of range for {weapon.ItemName}.");
        if (!attacker.TrySpendActionPoints(BattleSession.DefaultAttackActionPointCost))
          return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"{attacker.Combatant.Name} could not spend {BattleSession.DefaultAttackActionPointCost} action points.");

        AttackContext context = new(attacker, Target, attackerPoint, targetPoint, weapon, session.Board);
        HitChanceBreakdown breakdown = session.HitChanceCalculator.Calculate(context);
        int roll = session.RollPercent();
        bool isHit = roll < breakdown.FinalChance;

        session.RaiseEvent(new UnitAttackedBattleEvent(attacker, Target, targetPoint, weapon, breakdown, roll, isHit));
        if (isHit)
          session.ApplyDamageTo(Target, weapon.EmitDamage());

        return BattleActionResult.Success(this, attacker);
      });
  }
}

public sealed class ApplyDamage : BattleAction
{
  public const string ApplyDamageActionId = "apply_damage";

  public BattleUnitState Unit { get; }
  public int Amount { get; }

  internal ApplyDamage(BattleUnitState unit, int amount)
    : base(ApplyDamageActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Amount = amount;
  }

  internal override BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (!Unit.IsAlive)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit {Unit.Id} is not alive.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    if (unitPointOption.IsNone)
      return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Damage action could not resolve position for unit {Unit.Id}.");

    session.ApplyDamageTo(Unit, Amount);

    return BattleActionResult.Success(this, Unit);
  }
}

public sealed class PassUnit : BattleAction
{
  public const string PassUnitActionId = "pass_unit";

  public BattleUnitState Unit { get; }

  internal PassUnit(BattleUnitState unit)
    : base(PassUnitActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override BattleActionResult Execute(BattleSession session)
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

  internal EndFactionTurn(Faction expectedActiveSide)
    : base(EndFactionTurnActionId)
  {
    ArgumentNullException.ThrowIfNull(expectedActiveSide);
    ExpectedActiveSide = expectedActiveSide;
  }

  internal override BattleActionResult Execute(BattleSession session)
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
