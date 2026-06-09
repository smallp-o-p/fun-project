using FunProject.Combatants;
using FunProject.Items;
using System;

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

public interface ISourcePositionedBattleEvent : BattleEventTag
{
  BattleBoardState.ValidatedPoint SourcePosition { get; }
}

public interface IFactionBattleEvent : BattleEventTag
{
  Faction Faction { get; }
}

public interface ITurnBattleEvent : BattleEventTag
{
  int TurnNumber { get; }
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

public sealed record TurnStartedBattleEvent : BattleEvent, IFactionBattleEvent, ITurnBattleEvent
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

public sealed record TurnEndedBattleEvent : BattleEvent, IFactionBattleEvent, ITurnBattleEvent
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

public sealed record ActiveSideChangedBattleEvent : BattleEvent, IFactionBattleEvent
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

public sealed record UnitMovedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent, ISourcePositionedBattleEvent
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
  public int Amount { get; }

  public UnitDamagedBattleEvent(BattleUnitState unit, int amount)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Amount = amount;
  }

  public override string ToDisplayString() => $"Damage: {Amount}";
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
  public ThrowableItem Item { get; }

  public ItemThrownBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position, ThrowableItem item)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(item);
    Unit = unit;
    Position = position;
    Item = item;
  }

  public override string ToDisplayString() => $"{Unit.Combatant.Name} threw {Item.ItemName}.";
}
