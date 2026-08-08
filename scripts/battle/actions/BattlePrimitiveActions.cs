using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using System;
using System.Linq;
namespace FunProject.Battle;

public sealed class StartBattle : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Setup || !session.AliveUnits.Any())
      return Result.Rejected;

    session.StartBattle();
    return Result.Completed;
  }
}

public sealed class SpawnUnit : BattleAction
{
  private Combatant Combatant { get; }
  private BattleBoardState.ValidatedPoint Position { get; }
  private Option<Weapon> EquippedWeapon { get; }
  private Option<ItemWith<ArmorCapability>> EquippedArmor { get; }

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
  {
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    Position = position;
    EquippedWeapon = equippedWeapon;
    EquippedArmor = equippedArmor;
  }

  public override Result Execute(BattleSession session)
  {
    if (session.Phase == BattlePhase.Ended || !session.Board.CanOccupy(Position))
      return Result.Rejected;

    session.AddUnit(Combatant, Position, EquippedWeapon, EquippedArmor);
    return Result.Completed;
  }
}

// Target feasibility (weapon/self/ally/liveness/visibility/range) resolves once through
// AttackContext.Resolve — the gate shared with GetHitChanceForAttack and
// HasAttackableTargetCondition — so the write side can never drift from the preview.
// A feasibility miss interrupts the action silently rather than rejecting: interrupts
// interleave inside one submission, so a target can die, move out of range/sight, or the
// magazine can be spent by an earlier interrupt after this action was constructed — a
// quiet drop is preferred to unwinding the whole submission over a normal reaction chain.
public sealed class AttackUnit(AliveUnit attacker, AliveUnit target) : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    // A dead attacker has no board position for Resolve to read; that staleness (killed by
    // an earlier interrupt) interrupts quietly like every other mutable fact.
    if (session.TryGetAlive(attacker.State).IsNone)
      return Result.Interrupted;

    return AttackContext.Resolve(session, attacker.State, target.State).Match(
      _ => Result.Interrupted,
      context => Fire(session, attacker, target, context));
  }

  private static Result Fire(BattleSession session, AliveUnit attacker, AliveUnit target, AttackContext context)
  {
    // Ammo is mutable state between construction and commit: another interrupt in this
    // submission may have fired the same weapon. An empty magazine interrupts quietly
    // before anything is spent.
    if (!context.Weapon.IsLoaded)
      return Result.Interrupted;

    attacker.State.SpendActionPoints(BattleSession.DefaultAttackActionPointCost);

    SysColGeneric.List<Damage> bundle = context.Weapon.TrySpendShot(attacker.State.ActiveBuffDamageMods).Match(
      Some: damage => damage,
      None: () => throw new InvalidOperationException(
        $"{context.Weapon.ItemName} could not spend a shot."));

    HitChanceBreakdown breakdown = session.HitChanceCalculator.Calculate(context);
    int roll = session.RollPercent();
    bool isHit = roll < breakdown.FinalChance;

    session.RaiseEvents(new UnitAttackedBattleEvent(
      attacker.State,
      target.State,
      context.DefenderPosition,
      context.Weapon,
      breakdown,
      roll,
      isHit));

    if (isHit)
      session.ApplyDamageTo(target.State, bundle, Some(attacker.State));

    return Result.Completed;
  }
}

// Throws a carried throwable to a target tile. Target legality (in-bounds via ValidatedPoint,
// range per the throwable's ThrowRange) is the caller's job — the same trust split as the rest
// of the primitive actions. Consumption is parsed once at construction into a Consumable; the
// blast payload is resolved by CapabilityEffectSystem reacting to ItemThrownBattleEvent.
public sealed class ThrowItem(
  AliveUnit unit,
  ItemWith<ThrowableCapability> throwable,
  BattleBoardState.ValidatedPoint targetCell) : BattleAction
{
  private readonly Option<Consumable> _consumption = MakeConsumption(throwable);

  private static Option<Consumable> MakeConsumption(ItemWith<ThrowableCapability> proof)
  {
    ArgumentNullException.ThrowIfNull(proof.Item); // guards default-constructed proof structs
    ArgumentNullException.ThrowIfNull(proof.Capability);
    return Consumable.From(proof);
  }

  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    // Liveness and possession can change between construction and execution when earlier
    // interrupts in the same submission interpose (death, item spent/removed elsewhere).
    if (session.TryGetAlive(unit.State).IsNone || !unit.State.HasInventoryItem(throwable.Item))
      return Result.Interrupted;

    unit.State.SpendActionPoints(throwable.Capability.ActionPointCost);
    _consumption.IfSome(consumable => consumable.SpendOnce(unit.State));

    session.RaiseEvents(new ItemThrownBattleEvent(unit.State, targetCell, throwable.Item));
    return Result.Completed;
  }
}

// The generic active-item verb: spends one charge of any explicitly charged item at the
// session's default use cost and raises ItemUsedBattleEvent. Effect payloads (heals,
// deployables, ...) belong to hooks reacting to that event, not to this action.
public sealed class UseItem(AliveUnit unit, ItemWith<ChargesCapability> usable) : BattleAction
{
  private readonly Consumable _consumption = MakeConsumable(usable);

  private static Consumable MakeConsumable(ItemWith<ChargesCapability> proof)
  {
    ArgumentNullException.ThrowIfNull(proof.Item);
    ArgumentNullException.ThrowIfNull(proof.Capability);
    return Consumable.From(proof);
  }

  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    // Liveness, possession, and depletion are re-checked before any mutation: earlier
    // interrupts in the same submission can kill the actor or spend/remove the item, and
    // a depleted use must not spend AP (or throw) mid-submission.
    if (session.TryGetAlive(unit.State).IsNone
        || !unit.State.HasInventoryItem(usable.Item)
        || usable.Capability.IsDepleted)
    {
      return Result.Interrupted;
    }

    unit.State.SpendActionPoints(BattleSession.DefaultUseItemActionPointCost);
    _consumption.SpendOnce(unit.State);

    session.RaiseEvents(new ItemUsedBattleEvent(unit.State, usable.Item));
    return Result.Completed;
  }
}

public sealed class ApplyDamage : BattleAction
{
  private AliveUnit Unit { get; }
  public int Amount { get; }

  internal ApplyDamage(AliveUnit unit, int amount)
  {
    Unit = unit;
    Amount = amount;
  }

  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    // Interleaved interrupts can kill the target after this action was constructed; a
    // stale proof interrupts quietly instead of throwing.
    if (session.TryGetAlive(Unit.State).IsNone)
      return Result.Interrupted;

    session.ApplyDamageTo(Unit.State, Amount);

    return Result.Completed;
  }
}

public sealed class PassUnit(AliveUnit unit) : BattleAction
{

  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.TryGetAlive(unit.State).IsNone)
      return Result.Interrupted;

    session.EndUnitActivation(unit.State);
    return Result.Completed;
  }
}

public sealed class EndFactionTurn(Faction expectedActiveSide) : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return Result.Rejected;

    Faction activeSide = session.ActiveSide;
    if (activeSide != expectedActiveSide)
      return Result.Rejected;

    session.EndFactionTurn(expectedActiveSide);
    return Result.Completed;
  }
}

public sealed class ReloadWeapon(AliveUnit unit, AmmunitionedWeapon weapon) : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    if (!weapon.CanReload())
      return Result.Rejected;

    // A death or weapon swap earlier in the same submission invalidates the proof/equipment
    // pair; a stale reload interrupts quietly.
    if (session.TryGetAlive(unit.State).IsNone || !unit.State.EquippedWeapon.Contains(weapon))
      return Result.Interrupted;

    unit.State.SpendActionPoints(BattleSession.DefaultReloadActionPointCost);

    weapon.Reload();
    session.RaiseEvents(new UnitReloadedWeaponBattleEvent(unit.State, weapon));
    return Result.Completed;
  }
}
