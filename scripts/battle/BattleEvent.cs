using FunProject.Combatants;
using FunProject.Items;
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
  public override string ToDisplayString() => "Battle ended.";
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

public sealed record UnitDamagedBattleEvent : BattleEvent, IUnitBattleEvent
{
  public override string EventName => "unit_damaged";
  public BattleUnitState Unit { get; }
  public IReadOnlyList<Damage> Bundle { get; }
  public int TotalAmount { get; }

  public UnitDamagedBattleEvent(BattleUnitState unit, IReadOnlyList<Damage> bundle)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    Unit = unit;
    Bundle = bundle;
    TotalAmount = bundle.Sum(damage => damage.Amount);
  }

  public override string ToDisplayString() => $"Damage: {TotalAmount}";
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
