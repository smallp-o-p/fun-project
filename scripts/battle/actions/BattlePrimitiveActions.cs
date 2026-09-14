using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using System;
using System.Collections.Generic;
namespace FunProject.Battle;

public sealed class StartBattle : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.Setup || !session.AliveUnits.AsValueEnumerable().Any())
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
  private IReadOnlyList<StatMod> StatMods { get; }

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
    Option<ItemWith<ArmorCapability>> equippedArmor,
    IReadOnlyList<StatMod>? statMods = null)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    Position = position;
    EquippedWeapon = equippedWeapon;
    EquippedArmor = equippedArmor;
    StatMods = statMods ?? [];
  }

  public override Result Execute(BattleSession session)
  {
    if (session.Phase == BattlePhase.Ended || !session.Board.CanOccupy(Position))
      return Result.Rejected;

    session.AddUnit(Combatant, Position, EquippedWeapon, EquippedArmor, StatMods);
    return Result.Completed;
  }
}

/// <summary>Places a special board object onto the board during setup.</summary>
public sealed class PlaceObject(BattleSpecialObjectData data, BattleBoardState.ValidatedPoint position) : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (session.Phase != BattlePhase.Setup || !session.Board.CanOccupy(position))
      return Result.Rejected;

    session.AddObject(data, position);
    return Result.Completed;
  }
}

/// <summary>Interacts with a live board object, spending AP before mutating it. Adjacency
/// and other reach rules are deliberately NOT enforced here — they belong to a higher-level
/// action wrapping this primitive.</summary>
public sealed class InteractWithObject(AliveUnit unit, LiveObject obj) : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.TryGetAlive(unit.State).IsNone || unit.State.IsIncapacitated)
      return Result.Interrupted;
    if (session.TryGetAliveObject(obj.State).IsNone)
      return Result.Interrupted;

    return obj.State.FindCapability<InteractiveCapability>().Match(
      Some: interactive =>
      {
        if (!unit.State.TrySpendActionPoints(interactive.ActionPointCost))
          return Result.Rejected;

        session.MarkObjectInteracted(obj.State);
        session.RaiseEvents(new ObjectInteractedBattleEvent(unit.State, obj.State, obj.Position));
        return Result.Completed;
      },
      None: () => Result.Rejected);
  }
}

// Target feasibility (weapon/self/ally/liveness/visibility/range) resolves once through
// AttackContext.Resolve — the gate shared with GetHitChanceForAttack — so the write side
// can never drift from the preview.
// A feasibility miss interrupts the action silently rather than rejecting: interrupts
// interleave inside one submission, so a target can die, move out of range/sight, or the
// magazine can be spent by an earlier interrupt after this action was constructed — a
// quiet drop is preferred to unwinding the whole submission over a normal reaction chain.
public sealed class AttackUnit(AliveUnit attacker, AliveUnit target) : BattleAction
{
  public override Result Execute(BattleSession session)
  {
    // A dead or incapacitated attacker has no right to perform a new action; that staleness
    // (killed or disabled by an earlier interrupt) interrupts quietly like every other mutable
    // fact.
    if (session.TryGetAlive(attacker.State).IsNone || attacker.State.IsIncapacitated)
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

    // Liveness, incapacitation, and possession can change between construction and execution
    // when earlier interrupts in the same submission interpose (death, disablement, item
    // spent/removed elsewhere).
    if (session.TryGetAlive(unit.State).IsNone || unit.State.IsIncapacitated)
      return Result.Interrupted;
    if (!unit.State.HasInventoryItem(throwable.Item))
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

    // Liveness, incapacitation, possession, and depletion are re-checked before any mutation:
    // earlier interrupts in the same submission can kill/disable the actor or spend/remove the
    // item, and a depleted use must not spend AP (or throw) mid-submission.
    if (session.TryGetAlive(unit.State).IsNone || unit.State.IsIncapacitated)
    {
      return Result.Interrupted;
    }
    if (!unit.State.HasInventoryItem(usable.Item) || usable.Capability.IsDepleted)
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
  public DamageKind Kind { get; }

  internal ApplyDamage(AliveUnit unit, int amount, DamageKind kind = DamageKind.Health)
  {
    Unit = unit;
    Amount = amount;
    Kind = kind;
  }

  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    // Interleaved interrupts can kill the target after this action was constructed; a
    // stale proof interrupts quietly instead of throwing.
    if (session.TryGetAlive(Unit.State).IsNone)
      return Result.Interrupted;

    session.ApplyDamageTo(Unit.State, Amount, Kind);

    return Result.Completed;
  }
}

public sealed class PassUnit(AliveUnit unit) : BattleAction
{

  public override Result Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.TryGetAlive(unit.State).IsNone || unit.State.IsIncapacitated)
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
    if (session.TryGetAlive(unit.State).IsNone || unit.State.IsIncapacitated)
      return Result.Interrupted;
    if (!weapon.CanReload())
      return Result.Rejected;

    // A death or weapon swap earlier in the same submission invalidates the proof/equipment
    // pair; a stale reload interrupts quietly.
    if (!unit.State.EquippedWeapon.Contains(weapon))
      return Result.Interrupted;

    unit.State.SpendActionPoints(BattleSession.DefaultReloadActionPointCost);

    weapon.Reload();
    session.RaiseEvents(new UnitReloadedWeaponBattleEvent(unit.State, weapon));
    return Result.Completed;
  }
}
