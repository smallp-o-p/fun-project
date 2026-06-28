using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Effects;
using FunProject.Weapons;
using System;
using System.Collections.Generic;
using System.Linq;

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
/// Carries one resolved damage application. Bundle and TotalAmount describe the
/// pre-mitigation payload; ArmorDamage and HealthDamage are the amounts actually
/// applied after armor resolution. They need not reconcile: an element-matched hit
/// strips armor at 1.5x, so ArmorDamage can exceed TotalAmount.
/// </summary>
public sealed record UnitDamagedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public BattleUnitState Unit { get; }
  public IReadOnlyList<Damage> Bundle { get; }
  public int TotalAmount { get; }
  public int ArmorDamage { get; }
  public int HealthDamage { get; }

  public UnitDamagedBattleEvent(BattleUnitState unit, IReadOnlyList<Damage> bundle, int armorDamage, int healthDamage)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    ArgumentOutOfRangeException.ThrowIfNegative(armorDamage);
    ArgumentOutOfRangeException.ThrowIfNegative(healthDamage);
    Unit = unit;
    Bundle = bundle;
    TotalAmount = bundle.Sum(damage => damage.Amount);
    ArmorDamage = armorDamage;
    HealthDamage = healthDamage;
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

public sealed record UnitKilledBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitKilledBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
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

public sealed record OperationCompletedBattleEvent : BattleEvent
{
  public Faction Faction { get; }

  public OperationCompletedBattleEvent(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }
}

public sealed record OperationFailedBattleEvent : BattleEvent
{
  public Faction Faction { get; }

  public OperationFailedBattleEvent(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }
}
