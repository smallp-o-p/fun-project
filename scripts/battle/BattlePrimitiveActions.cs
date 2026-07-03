using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;
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
  public BattleBoardState.ValidatedPoint Position { get; }
  public Option<Weapon> EquippedWeapon { get; }
  public Option<ItemWith<ArmorCapability>> EquippedArmor { get; }

  internal SpawnUnit(Combatant combatant, BattleBoardState.ValidatedPoint position)
    : this(combatant, position, None, None)
  {
  }

  internal SpawnUnit(Combatant combatant, BattleBoardState.ValidatedPoint position, Weapon equippedWeapon)
    : this(combatant, position, Some(equippedWeapon), None)
  {
  }

  internal SpawnUnit(
    Combatant combatant,
    BattleBoardState.ValidatedPoint position,
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

    // In-bounds is proven by the ValidatedPoint; only occupancy (mutable) is checked here.
    if (!session.Board.CanOccupy(Position))
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Cannot place a unit at {Position.Raw}.");

    var spawnedUnit = session.AddUnit(Combatant, Position, EquippedWeapon, EquippedArmor);
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

    var validationFailure = ValidateActingUnit(session, Unit, ActionPointCost);
    if (validationFailure.IsSome)
      return validationFailure.Value();

    Option<BattleBoardState.ValidatedPoint> sourcePointOption = session.GetUnitPosition(Unit);
    if (sourcePointOption.IsNone)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {UnitId} is not on a valid tile.");
    BattleBoardState.ValidatedPoint sourcePoint = sourcePointOption.Value();

    if (sourcePoint != Source)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {UnitId} is no longer at {Source.Raw}.");
    if (!session.Board.CanOccupy(Destination))
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{Destination} cannot be occupied.");
    if (!Unit.TrySpendActionPoints(ActionPointCost))
      return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"Move unit step action could not spend {ActionPointCost} action points for unit {UnitId}.");
    session.MoveUnit(Unit, Source, Destination);

    return BattleActionResult.Success(this, Unit);
  }
}

// Shared write-path lifecycle for "use an active item capability" verbs (throw today;
// future heal/buff/deploy). It owns the parts every active use repeats, in a fixed order:
// acting-unit validation (AP cost), the inventory-has-item gate, the consume-on-use charge
// depletion guard, spending action points, consuming a charge (removing the item when it
// depletes, or treating a charge-less consumable as single-use), and the success result.
// A verb supplies only its variable pieces via the abstract members below.
public abstract class UseItemCapabilityAction : BattleAction
{
  protected UseItemCapabilityAction(string actionId)
    : base(actionId)
  {
  }

  protected abstract BattleUnitState ActingUnit { get; }
  protected abstract EquippableItem UsedItem { get; }
  protected abstract int ActionPointCost { get; }
  protected abstract bool ConsumesOnUse { get; }

  // Verb target validation that runs before the inventory/charge gates: resolve whatever
  // the verb needs (positions, target cell, …) and reject illegal targets, stashing any
  // resolved values the later hooks consume. Some == rejection.
  protected abstract Option<BattleActionResult> ResolveTarget(BattleSession session);

  // Verb usability check that runs after the item is confirmed present and not depleted,
  // but before any state is committed (for throw: range). Kept separate from ResolveTarget
  // so the inventory/charge rejections keep priority over it, preserving the original order.
  protected abstract Option<BattleActionResult> ValidateUsability(BattleSession session);

  // Commit hook: raise the verb's use event, using values stashed during ResolveTarget.
  protected abstract void RaiseUseEvent(BattleSession session);

  internal sealed override BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    Option<BattleActionResult> validationFailure = ValidateActingUnit(session, ActingUnit, ActionPointCost);
    if (validationFailure.IsSome)
      return validationFailure.Value();

    Option<BattleActionResult> targetFailure = ResolveTarget(session);
    if (targetFailure.IsSome)
      return targetFailure.Value();

    if (!ActingUnit.HasInventoryItem(UsedItem))
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{ActingUnit.Combatant.Name} does not have {UsedItem.ItemName}.");

    Option<ChargesCapability> charges = UsedItem.FindCapability<ChargesCapability>();
    if (ConsumesOnUse && charges.Match(c => c.IsDepleted, () => false))
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{UsedItem.ItemName} has no charges remaining.");

    Option<BattleActionResult> usabilityFailure = ValidateUsability(session);
    if (usabilityFailure.IsSome)
      return usabilityFailure.Value();

    if (!ActingUnit.TrySpendActionPoints(ActionPointCost))
      return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"{ActingUnit.Combatant.Name} could not spend {ActionPointCost} action points.");

    if (ConsumesOnUse)
    {
      charges.Match(
        chargeState =>
        {
          if (!chargeState.TrySpend())
            throw new InvalidOperationException($"{UsedItem.ItemName} could not spend a charge after passing the depletion check.");

          if (chargeState.IsDepleted)
            ActingUnit.RemoveInventoryItem(UsedItem);
        },
        // No charges capability: consumable items are implicitly single-use.
        () => ActingUnit.RemoveInventoryItem(UsedItem));
    }

    RaiseUseEvent(session);
    return BattleActionResult.Success(this, ActingUnit);
  }
}

public sealed class ThrowItem : UseItemCapabilityAction
{
  public const string ThrowItemActionId = "throw_item";

  public BattleUnitState Unit { get; }
  public ItemWith<ThrowableCapability> Throwable { get; }
  public BattleBoardState.ValidatedPoint TargetCell { get; }

  public EquippableItem Item => Throwable.Item;

  // Value resolved in ResolveTarget and consumed by ValidateUsability within the same
  // Execute pass.
  private Vector3I _unitPosition;

  internal ThrowItem(BattleUnitState unit, ItemWith<ThrowableCapability> throwable, BattleBoardState.ValidatedPoint targetCell)
    : base(ThrowItemActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(throwable.Item); // guards default-constructed proof structs
    ArgumentNullException.ThrowIfNull(throwable.Capability);

    Unit = unit;
    Throwable = throwable;
    TargetCell = targetCell;
  }

  protected override BattleUnitState ActingUnit => Unit;
  protected override EquippableItem UsedItem => Item;
  protected override int ActionPointCost => Throwable.Capability.ActionPointCost;
  protected override bool ConsumesOnUse => Throwable.Capability.ConsumesOnUse;

  protected override Option<BattleActionResult> ResolveTarget(BattleSession session)
  {
    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    if (unitPointOption.IsNone)
      return Some(BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit id {Unit.Id} is not on a valid tile."));
    _unitPosition = unitPointOption.Value().Raw;

    return None;
  }

  protected override Option<BattleActionResult> ValidateUsability(BattleSession session)
  {
    if (BattleSession.GetGridDistance(_unitPosition, TargetCell.Raw) > Throwable.Capability.ThrowRange)
      return Some(BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{TargetCell.Raw} is out of range for {Item.ItemName}."));

    return None;
  }

  protected override void RaiseUseEvent(BattleSession session)
  {
    session.RaiseEvent(new ItemThrownBattleEvent(Unit, TargetCell, Item));
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
    var validationFailure = ValidateActingUnit(session, Unit, BattleSession.DefaultAttackActionPointCost);
    if (validationFailure.IsSome)
      return validationFailure.Value();

    return AttackFeasibility.Resolve(session, Unit, Target).Match(
      Left: failure => BattleActionResult.Failure(this, MapFailureReason(failure.Kind), failure.Message),
      Right: resolved => ResolveAttack(session, resolved));
  }

  private static BattleActionFailureReason MapFailureReason(AttackFeasibilityFailureKind kind) =>
    kind == AttackFeasibilityFailureKind.PositionUnresolved
      ? BattleActionFailureReason.UnexpectedError
      : BattleActionFailureReason.Rejected;

  private BattleActionResult ResolveAttack(BattleSession session, ResolvedAttack resolved)
  {
    // Ammo gate comes before the AP spend: an empty magazine is a legal rejection
    // that must leave the attacker's state fully untouched.
    return resolved.Weapon.TrySpendShot().Match(
      Some: bundle => CommitAttack(session, resolved, bundle),
      None: () => BattleActionResult.Failure(this, BattleActionFailureReason.Rejected,
        $"{resolved.Weapon.ItemName} has no ammunition loaded."));
  }

  private BattleActionResult CommitAttack(BattleSession session, ResolvedAttack resolved, List<Damage> bundle)
  {
    BattleUnitState attacker = Unit;

    if (!attacker.TrySpendActionPoints(BattleSession.DefaultAttackActionPointCost))
      return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError, $"{attacker.Combatant.Name} could not spend {BattleSession.DefaultAttackActionPointCost} action points.");

    AttackContext context = new(attacker, resolved.AttackerPoint, resolved.TargetPoint, session.Board);
    HitChanceBreakdown breakdown = session.HitChanceCalculator.Calculate(context);
    int roll = session.RollPercent();
    bool isHit = roll < breakdown.FinalChance;

    session.RaiseEvent(new UnitAttackedBattleEvent(attacker, Target, resolved.TargetPoint, resolved.Weapon, breakdown, roll, isHit));
    if (isHit)
      session.ApplyDamageTo(Target, bundle, Some(Unit));

    return BattleActionResult.Success(this, attacker);
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
    var validationFailure = ValidateActingUnit(session, Unit);
    if (validationFailure.IsSome)
      return validationFailure.Value();

    session.EndUnitActivation(Unit);
    return BattleActionResult.Success(this, Unit);
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

public sealed class ReloadWeapon : BattleAction
{
  public const string ReloadWeaponActionId = "reload_weapon";

  public BattleUnitState Unit { get; }

  internal ReloadWeapon(BattleUnitState unit) : base(ReloadWeaponActionId)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override BattleActionResult Execute(BattleSession session)
  {
    var validationFailure = ValidateActingUnit(session, Unit, BattleSession.DefaultReloadActionPointCost);
    if (validationFailure.IsSome)
      return validationFailure.Value();

    // The reload event carries the concrete magazine weapon, so the write side resolves it here.
    Option<AmmunitionedWeapon> weaponOption = Unit.EquippedWeapon.Bind(
      weapon => Optional(weapon as AmmunitionedWeapon));
    if (weaponOption.IsNone)
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected,
        $"{Unit.Combatant.Name} has no reloadable weapon.");
    AmmunitionedWeapon reloadable = weaponOption.Match(
      weapon => weapon,
      () => throw new InvalidOperationException("Reloadable weapon vanished."));

    if (!reloadable.CanReload())
      return BattleActionResult.Failure(this, BattleActionFailureReason.Rejected,
        $"{reloadable.ItemName} is already fully loaded.");
    if (!Unit.TrySpendActionPoints(BattleSession.DefaultReloadActionPointCost))
      return BattleActionResult.Failure(this, BattleActionFailureReason.UnexpectedError,
        $"{Unit.Combatant.Name} could not spend {BattleSession.DefaultReloadActionPointCost} action points.");

    reloadable.Reload();
    session.RaiseEvent(new UnitReloadedWeaponBattleEvent(Unit, reloadable));
    return BattleActionResult.Success(this, Unit);
  }
}
