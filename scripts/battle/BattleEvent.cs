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
  public abstract string EventName { get; }
  public abstract string ToDisplayString();
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
  public override string EventName => "session_started";
  public override string ToDisplayString() => "Battle started.";
}

public sealed record SessionEndedBattleEvent : BattleEvent
{
  public override string EventName => "session_ended";
  public BattleOutcome Outcome { get; }

  public SessionEndedBattleEvent(BattleOutcome outcome = BattleOutcome.Draw)
  {
    Outcome = outcome;
  }

  public override string ToDisplayString() => Outcome switch
  {
    BattleOutcome.Victory => "Battle won.",
    BattleOutcome.Defeat => "Battle lost.",
    _ => "Battle ended.",
  };
}

public sealed record TurnStartedBattleEvent : BattleEvent
{
  public override string EventName => "turn_started";
  public Faction Faction { get; }
  public int TurnNumber { get; }

  public TurnStartedBattleEvent(Faction faction, int turnNumber)
  {
    Faction = faction;
    TurnNumber = turnNumber;
  }

  public override string ToDisplayString() => $"Turn {TurnNumber} started for {Faction.Name}.";
}

public sealed record TurnEndedBattleEvent : BattleEvent
{
  public override string EventName => "turn_ended";
  public Faction Faction { get; }
  public int TurnNumber { get; }

  public TurnEndedBattleEvent(Faction faction, int turnNumber)
  {
    Faction = faction;
    TurnNumber = turnNumber;
  }

  public override string ToDisplayString() => $"Turn {TurnNumber} ended for {Faction.Name}.";
}

public sealed record ActiveSideChangedBattleEvent : BattleEvent
{
  public override string EventName => "active_side_changed";
  public Faction Faction { get; }

  public ActiveSideChangedBattleEvent(Faction faction)
  {
    Faction = faction;
  }

  public override string ToDisplayString() => $"Active side is now {Faction.Name}.";
}

public sealed record UnitAddedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "unit_added";
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitAddedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }

  public override string ToDisplayString() => $"{Unit.Combatant.Name} entered the battle at {Position}.";
}

public sealed record UnitActivationEndedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "unit_activation_ended";
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitActivationEndedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }

  public override string ToDisplayString() => $"{Unit.Combatant.Name} ended their activation.";
}

public sealed record UnitMovedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "unit_moved";
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

  public override string ToDisplayString() => $"Unit ID {Unit.Id} moved from {SourcePosition} to {Position}.";
}

public sealed record TileOccupiedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "tile_occupied";
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

  public override string ToDisplayString() => $"Unit ID {Unit.Id} occupied {Position}.";
}

/// <summary>
/// Carries one resolved damage application. Bundle and TotalAmount describe the
/// pre-mitigation payload; ArmorDamage and HealthDamage are the amounts actually
/// applied after armor resolution. They need not reconcile: an element-matched hit
/// strips armor at 1.5x, so ArmorDamage can exceed TotalAmount.
/// </summary>
public sealed record UnitDamagedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public override string EventName => "unit_damaged";
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

  public override string ToDisplayString() => $"Damage: {TotalAmount} ({ArmorDamage} armor, {HealthDamage} health)";
}

public sealed record UnitArmorRegeneratedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public override string EventName => "unit_armor_regenerated";
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

  public override string ToDisplayString() => $"Unit ID {Unit.Id} regenerated {AmountRegenerated} armor ({CurrentArmor} current).";
}

public sealed record UnitKilledBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "unit_killed";
  public BattleUnitState Unit { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitKilledBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }

  public override string ToDisplayString() => $"Unit ID {Unit.Id} was killed!";
}

public sealed record ItemThrownBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "item_thrown";
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

  public override string ToDisplayString() => $"{Unit.Combatant.Name} threw {Item.ItemName}.";
}

public sealed record UnitAttackedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public override string EventName => "unit_attacked";
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

  public override string ToDisplayString() =>
    $"{Unit.Combatant.Name} attacked {Target.Combatant.Name} ({Breakdown.FinalChance}% to hit, rolled {Roll}): {(IsHit ? "hit" : "miss")}.";
}

public sealed record UnitStatusEffectAppliedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public override string EventName => "unit_status_effect_applied";
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

  public override string ToDisplayString() => $"{Unit.Combatant.Name} is afflicted by {Spec.Name} ({RemainingTurns} turns).";
}

public sealed record UnitStatusEffectTickedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public override string EventName => "unit_status_effect_ticked";
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

  public override string ToDisplayString() => $"{Spec.Name} ticks on {Unit.Combatant.Name} ({RemainingTurns} turns left).";
}

public sealed record UnitStatusEffectExpiredBattleEvent : BattleEvent, IUnitBattleEvent
{
  public override string EventName => "unit_status_effect_expired";
  public BattleUnitState Unit { get; }
  public StatusEffectSpecData Spec { get; }

  public UnitStatusEffectExpiredBattleEvent(BattleUnitState unit, StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(spec);
    Unit = unit;
    Spec = spec;
  }

  public override string ToDisplayString() => $"{Spec.Name} wore off {Unit.Combatant.Name}.";
}

public sealed record ObjectiveAddedBattleEvent : BattleEvent
{
  public override string EventName => "objective_added";
  public Faction Faction { get; }
  public Objective Objective { get; }

  public ObjectiveAddedBattleEvent(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    Faction = faction;
    Objective = objective;
  }

  public override string ToDisplayString() => $"{Faction.Name} received objective {Objective.Data.Name}.";
}

public sealed record ObjectiveCompletedBattleEvent : BattleEvent
{
  public override string EventName => "objective_completed";
  public Faction Faction { get; }
  public Objective Objective { get; }

  public ObjectiveCompletedBattleEvent(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    Faction = faction;
    Objective = objective;
  }

  public override string ToDisplayString() => $"{Faction.Name} completed objective {Objective.Data.Name}.";
}

public sealed record ObjectiveFailedBattleEvent : BattleEvent
{
  public override string EventName => "objective_failed";
  public Faction Faction { get; }
  public Objective Objective { get; }

  public ObjectiveFailedBattleEvent(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    Faction = faction;
    Objective = objective;
  }

  public override string ToDisplayString() => $"{Faction.Name} failed objective {Objective.Data.Name}.";
}

public sealed record OperationCompletedBattleEvent : BattleEvent
{
  public override string EventName => "operation_completed";
  public Faction Faction { get; }

  public OperationCompletedBattleEvent(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  public override string ToDisplayString() => $"{Faction.Name} completed its operation.";
}

public sealed record OperationFailedBattleEvent : BattleEvent
{
  public override string EventName => "operation_failed";
  public Faction Faction { get; }

  public OperationFailedBattleEvent(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  public override string ToDisplayString() => $"{Faction.Name}'s operation failed.";
}
