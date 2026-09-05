using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Effects;
using FunProject.Weapons;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public interface BattleEventTag
{
}

public abstract record BattleEvent : BattleEventTag
{
}

public interface IUnitBattleEvent : BattleEventTag
{
  BattleUnitState Unit { get; }
}

public interface IPositionedBattleEvent : BattleEventTag
{
  BattleBoardState.ValidatedPoint Position { get; }
}

public sealed record SessionStartedBattleEvent : BattleEvent
{
}

public sealed record SessionEndedBattleEvent : BattleEvent
{
  public BattleOutcome Outcome { get; }

  public SessionEndedBattleEvent(BattleOutcome outcome = BattleOutcome.Draw)
  {
    Outcome = outcome;
  }
}

/// <summary>
/// Event that may have been caused by a unit.
/// </summary>
public interface ICausedByUnit : BattleEventTag
{
  Option<BattleUnitState> MaybeCause { get; }
}

public sealed record TurnStartedBattleEvent : BattleEvent
{
  public Faction Faction { get; }
  public int TurnNumber { get; }

  public TurnStartedBattleEvent(Faction faction, int turnNumber)
  {
    Faction = faction;
    TurnNumber = turnNumber;
  }
}

public sealed record TurnEndedBattleEvent : BattleEvent
{
  public Faction Faction { get; }
  public int TurnNumber { get; }

  public TurnEndedBattleEvent(Faction faction, int turnNumber)
  {
    Faction = faction;
    TurnNumber = turnNumber;
  }
}

public sealed record ActiveSideChangedBattleEvent : BattleEvent
{
  public Faction Faction { get; }

  public ActiveSideChangedBattleEvent(Faction faction)
  {
    Faction = faction;
  }
}

public sealed record UnitAddedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitAddedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }
}

/// <summary>Raised when a special board object is placed onto a tile.</summary>
public sealed record ObjectPlacedBattleEvent : BattleEvent, IPositionedBattleEvent
{
  public BattleObjectState Object { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public ObjectPlacedBattleEvent(BattleObjectState @object, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(@object);
    Object = @object;
    Position = position;
  }
}

/// <summary>Raised when a unit successfully interacts with a placed object.</summary>
public sealed record ObjectInteractedBattleEvent : BattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Actor { get; }
  public BattleObjectState Object { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public ObjectInteractedBattleEvent(BattleUnitState actor, BattleObjectState @object, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(actor);
    ArgumentNullException.ThrowIfNull(@object);
    Actor = actor;
    Object = @object;
    Position = position;
  }
}

/// <summary>Raised after an object's expiry trigger resolves its payload.</summary>
public sealed record ObjectExpiredBattleEvent : BattleEvent, IPositionedBattleEvent
{
  public BattleObjectState Object { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public ObjectExpiredBattleEvent(BattleObjectState @object, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(@object);
    Object = @object;
    Position = position;
  }
}

public sealed record UnitActivationEndedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitActivationEndedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }
}

public sealed record UnitMovedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }
  public BattleBoardState.ValidatedPoint SourcePosition { get; }

  public UnitMovedBattleEvent(
    BattleUnitState unit,
    BattleBoardState.ValidatedPoint position,
    BattleBoardState.ValidatedPoint sourcePosition)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
    SourcePosition = sourcePosition;
  }
}

public sealed record TileOccupiedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public TileOccupiedBattleEvent(
    BattleUnitState unit,
    BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }
}

/// <summary>
/// Carries one resolved damage application.
/// </summary>
public sealed record UnitDamagedBattleEvent : BattleEvent, IUnitBattleEvent, ICausedByUnit
{
  public BattleUnitState Unit { get; }
  public IReadOnlyList<Damage> Bundle { get; }
  public int TotalAmount { get; }
  public int ArmorDamage { get; }
  public int HealthDamage { get; }
  public Option<BattleUnitState> MaybeCause { get; }

  public UnitDamagedBattleEvent(BattleUnitState unit, Option<BattleUnitState> cause, IReadOnlyList<Damage> bundle, int armorDamage, int healthDamage)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    ArgumentOutOfRangeException.ThrowIfNegative(armorDamage);
    ArgumentOutOfRangeException.ThrowIfNegative(healthDamage);
    Unit = unit;
    Bundle = bundle;
    TotalAmount = bundle.AsValueEnumerable().Sum(damage => damage.Amount);
    ArmorDamage = armorDamage;
    HealthDamage = healthDamage;
    MaybeCause = cause;
  }
}

public sealed record UnitArmorRegeneratedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public int AmountRegenerated { get; }
  public int CurrentArmor { get; }

  public UnitArmorRegeneratedBattleEvent(BattleUnitState unit, int amountRegenerated, int currentArmor)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountRegenerated);
    ArgumentOutOfRangeException.ThrowIfNegative(currentArmor);
    Unit = unit;
    AmountRegenerated = amountRegenerated;
    CurrentArmor = currentArmor;
  }
}

public sealed record UnitKilledBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent, ICausedByUnit
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }
  public Option<BattleUnitState> MaybeCause { get; }

  public UnitKilledBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position, Option<BattleUnitState> cause)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
    MaybeCause = cause;
  }
}

public sealed record ItemThrownBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }
  public EquippableItem Item { get; }

  public ItemThrownBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position, EquippableItem item)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(item);
    Unit = unit;
    Position = position;
    Item = item;
  }
}

/// <summary>
/// Raised when a unit uses an item through <c>UseItem</c> (the generic active-item verb).
/// Carries the acting unit and the item; effect payloads are resolved by hooks reacting to
/// this event, mirroring how blast effects follow <see cref="ItemThrownBattleEvent"/>.
/// </summary>
public sealed record ItemUsedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public EquippableItem Item { get; }

  public ItemUsedBattleEvent(BattleUnitState unit, EquippableItem item)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(item);
    Unit = unit;
    Item = item;
  }
}

/// <summary>
/// Raised after a thrown item's capability payload (e.g. a blast's effects) has been
/// resolved against the affected units. Carries the resolved item and the resolution
/// point so presentation can play an effect at that tile.
/// </summary>
public sealed record CapabilityResolvedBattleEvent : BattleEvent, IPositionedBattleEvent
{
  public EquippableItem Item { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public CapabilityResolvedBattleEvent(EquippableItem item, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(item);
    Item = item;
    Position = position;
  }
}

public sealed record UnitAttackedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleUnitState Target { get; }
  public BattleBoardState.ValidatedPoint Position { get; }
  public Weapon Weapon { get; }
  public HitChanceBreakdown Breakdown { get; }
  public int Roll { get; }
  public bool IsHit { get; }

  public UnitAttackedBattleEvent(
    BattleUnitState unit,
    BattleUnitState target,
    BattleBoardState.ValidatedPoint position,
    Weapon weapon,
    HitChanceBreakdown breakdown,
    int roll,
    bool isHit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(target);
    ArgumentNullException.ThrowIfNull(weapon);
    ArgumentNullException.ThrowIfNull(breakdown);
    Unit = unit;
    Target = target;
    Position = position;
    Weapon = weapon;
    Breakdown = breakdown;
    Roll = roll;
    IsHit = isHit;
  }
}

public sealed record UnitReloadedWeaponBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public AmmunitionedWeapon Weapon { get; }

  public UnitReloadedWeaponBattleEvent(BattleUnitState unit, AmmunitionedWeapon weapon)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(weapon);
    Unit = unit;
    Weapon = weapon;
  }
}

/// <summary>
/// Raised the first time an observer spots a target this battle. First-spotting is tracked PER
/// OBSERVER, so a second observer spotting an already-team-known target still raises this once for
/// that observer. <see cref="Unit"/> is the observer; <see cref="Target"/> is the unit it spotted.
/// </summary>
public sealed record UnitSpottedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleUnitState Target { get; }

  public UnitSpottedBattleEvent(BattleUnitState unit, BattleUnitState target)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(target);
    Unit = unit;
    Target = target;
  }
}

public sealed record UnitStatusEffectAppliedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public StatusEffectSpecData Spec { get; }
  public int RemainingTurns { get; }

  public UnitStatusEffectAppliedBattleEvent(BattleUnitState unit, StatusEffectSpecData spec, int remainingTurns)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(spec);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(remainingTurns);
    Unit = unit;
    Spec = spec;
    RemainingTurns = remainingTurns;
  }
}

public sealed record UnitStatusEffectTickedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public StatusEffectSpecData Spec { get; }
  public int RemainingTurns { get; }

  public UnitStatusEffectTickedBattleEvent(BattleUnitState unit, StatusEffectSpecData spec, int remainingTurns)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(spec);
    ArgumentOutOfRangeException.ThrowIfNegative(remainingTurns);
    Unit = unit;
    Spec = spec;
    RemainingTurns = remainingTurns;
  }
}

public sealed record UnitStatusEffectExpiredBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public StatusEffectSpecData Spec { get; }

  public UnitStatusEffectExpiredBattleEvent(BattleUnitState unit, StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(spec);
    Unit = unit;
    Spec = spec;
  }
}

public sealed record UnitBuffActivatedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public Buff Buff { get; }

  public UnitBuffActivatedBattleEvent(BattleUnitState unit, Buff buff)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(buff);
    Unit = unit;
    Buff = buff;
  }
}

public sealed record UnitBuffDeactivatedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public Buff Buff { get; }

  public UnitBuffDeactivatedBattleEvent(BattleUnitState unit, Buff buff)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(buff);
    Unit = unit;
    Buff = buff;
  }
}

public sealed record ObjectiveAddedBattleEvent : BattleEvent
{
  public Faction Faction { get; }
  public Objective Objective { get; }

  public ObjectiveAddedBattleEvent(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    Faction = faction;
    Objective = objective;
  }
}

public sealed record ObjectiveCompletedBattleEvent : BattleEvent
{
  public Faction Faction { get; }
  public Objective Objective { get; }

  public ObjectiveCompletedBattleEvent(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    Faction = faction;
    Objective = objective;
  }
}

public sealed record ObjectiveFailedBattleEvent : BattleEvent
{
  public Faction Faction { get; }
  public Objective Objective { get; }

  public ObjectiveFailedBattleEvent(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    Faction = faction;
    Objective = objective;
  }
}
