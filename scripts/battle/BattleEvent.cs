using FunProject.Items;
using System;

namespace FunProject.Battle;

public enum BattleEventType
{
  SessionStarted,
  SessionEnded,
  TurnStarted,
  TurnEnded,
  ActiveSideChanged,
  UnitAdded,
  UnitActivationEnded,
  UnitMoved,
  TileOccupied,
  UnitDamaged,
  UnitKilled,
  ItemThrown,
}

public abstract record BattleEvent
{
  public BattleEventType Type { get; }

  protected BattleEvent(BattleEventType type)
  {
    Type = type;
  }

  public abstract string ToDisplayString();
}

public interface IUnitBattleEvent
{
  BattleUnitState Unit { get; }
  int UnitId { get; }
}

public interface IPositionedBattleEvent
{
  BattleBoardState.ValidatedPoint Position { get; }
}

public interface ISourcePositionedBattleEvent
{
  BattleBoardState.ValidatedPoint SourcePosition { get; }
}

public interface IFactionBattleEvent
{
  Faction Faction { get; }
}

public interface ITurnBattleEvent
{
  int TurnNumber { get; }
}

public sealed record SessionStartedBattleEvent : BattleEvent
{
  public SessionStartedBattleEvent()
    : base(BattleEventType.SessionStarted)
  {
  }

  public override string ToDisplayString() => "Battle started.";
}

public sealed record SessionEndedBattleEvent : BattleEvent
{
  public SessionEndedBattleEvent()
    : base(BattleEventType.SessionEnded)
  {
  }

  public override string ToDisplayString() => "Battle ended.";
}

public sealed record TurnStartedBattleEvent : BattleEvent, IFactionBattleEvent, ITurnBattleEvent
{
  public Faction Faction { get; }
  public int TurnNumber { get; }

  public TurnStartedBattleEvent(Faction faction, int turnNumber)
    : base(BattleEventType.TurnStarted)
  {
    Faction = faction;
    TurnNumber = turnNumber;
  }

  public override string ToDisplayString() => $"Turn {TurnNumber} started for {Faction.Name}.";
}

public sealed record TurnEndedBattleEvent : BattleEvent, IFactionBattleEvent, ITurnBattleEvent
{
  public Faction Faction { get; }
  public int TurnNumber { get; }

  public TurnEndedBattleEvent(Faction faction, int turnNumber)
    : base(BattleEventType.TurnEnded)
  {
    Faction = faction;
    TurnNumber = turnNumber;
  }

  public override string ToDisplayString() => $"Turn {TurnNumber} ended for {Faction.Name}.";
}

public sealed record ActiveSideChangedBattleEvent : BattleEvent, IFactionBattleEvent
{
  public Faction Faction { get; }

  public ActiveSideChangedBattleEvent(Faction faction)
    : base(BattleEventType.ActiveSideChanged)
  {
    Faction = faction;
  }

  public override string ToDisplayString() => $"Active side is now {Faction.Name}.";
}

public sealed record UnitAddedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitAddedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
    : base(BattleEventType.UnitAdded)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }

  public override string ToDisplayString() => $"{Unit.Combatant.Name} entered the battle at {Position}.";
}

public sealed record UnitActivationEndedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitActivationEndedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
    : base(BattleEventType.UnitActivationEnded)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }

  public override string ToDisplayString() => $"{Unit.Combatant.Name} ended their activation.";
}

public sealed record UnitMovedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent, ISourcePositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }
  public BattleBoardState.ValidatedPoint SourcePosition { get; }

  public UnitMovedBattleEvent(
    BattleUnitState unit,
    BattleBoardState.ValidatedPoint position,
    BattleBoardState.ValidatedPoint sourcePosition)
    : base(BattleEventType.UnitMoved)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
    SourcePosition = sourcePosition;
  }

  public override string ToDisplayString() => $"Unit ID {UnitId} moved from {SourcePosition} to {Position}.";
}

public sealed record TileOccupiedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent, ISourcePositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }
  public BattleBoardState.ValidatedPoint SourcePosition { get; }

  public TileOccupiedBattleEvent(
    BattleUnitState unit,
    BattleBoardState.ValidatedPoint position,
    BattleBoardState.ValidatedPoint sourcePosition)
    : base(BattleEventType.TileOccupied)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
    SourcePosition = sourcePosition;
  }

  public override string ToDisplayString() => $"Unit ID {UnitId} occupied {Position}.";
}

public sealed record UnitDamagedBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }
  public int Amount { get; }

  public UnitDamagedBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position, int amount)
    : base(BattleEventType.UnitDamaged)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
    Amount = amount;
  }

  public override string ToDisplayString() => $"Damage: {Amount}";
}

public sealed record UnitKilledBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }

  public UnitKilledBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
    : base(BattleEventType.UnitKilled)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Position = position;
  }

  public override string ToDisplayString() => $"Unit ID {UnitId} was killed!";
}

public sealed record ItemThrownBattleEvent : BattleEvent, IUnitBattleEvent, IPositionedBattleEvent
{
  public BattleUnitState Unit { get; }
  public int UnitId => Unit.UnitId;
  public BattleBoardState.ValidatedPoint Position { get; }
  public ThrowableItem Item { get; }

  public ItemThrownBattleEvent(BattleUnitState unit, BattleBoardState.ValidatedPoint position, ThrowableItem item)
    : base(BattleEventType.ItemThrown)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(item);
    Unit = unit;
    Position = position;
    Item = item;
  }

  public override string ToDisplayString() => $"{Unit.Combatant.Name} threw {Item.ItemName}.";
}
