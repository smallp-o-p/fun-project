using FunProject.Combatants;
using System;
using System.Collections.Generic;
using System.Linq;
namespace FunProject.Battle;

public sealed class IsUnitVisibleToUnit : BattleSessionQuery<bool>
{
  public BattleUnitState ObserverUnit { get; }
  public BattleUnitState TargetUnit { get; }
  internal int ObserverUnitId => ObserverUnit.Id;
  internal int TargetUnitId => TargetUnit.Id;

  public IsUnitVisibleToUnit(BattleUnitState observerUnit, BattleUnitState targetUnit)
  {
    ArgumentNullException.ThrowIfNull(observerUnit);
    ArgumentNullException.ThrowIfNull(targetUnit);
    ObserverUnit = observerUnit;
    TargetUnit = targetUnit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    if (!ObserverUnit.IsAlive)
      return FailUnitNotAlive(ObserverUnit);
    if (!TargetUnit.IsAlive)
      return FailUnitNotAlive(TargetUnit);
    if (ObserverUnitId == TargetUnitId)
      return Succeed(true);

    return Succeed(ObserverUnit.VisibleUnits.Contains(TargetUnit));
  }
}

public sealed class IsUnitVisibleToFaction : BattleSessionQuery<bool>
{
  public Faction Faction { get; }
  public BattleUnitState TargetUnit { get; }
  internal int TargetUnitId => TargetUnit.Id;

  public IsUnitVisibleToFaction(Faction faction, BattleUnitState targetUnit)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(targetUnit);
    Faction = faction;
    TargetUnit = targetUnit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    if (!TargetUnit.IsAlive)
      return FailUnitNotAlive(TargetUnit);

    return Succeed(session.IsUnitVisibleToFaction(Faction, TargetUnit));
  }
}

public sealed class IsTileVisibleToFaction : BattleSessionQuery<bool>
{
  public Faction Faction { get; }
  public BattleBoardState.ValidatedPoint Tile { get; }

  public IsTileVisibleToFaction(Faction faction, BattleBoardState.ValidatedPoint tile)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
    Tile = tile;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    return Succeed(session.IsTileVisibleToFaction(Faction, Tile));
  }
}

public sealed class HasFactionExploredTile : BattleSessionQuery<bool>
{
  public Faction Faction { get; }
  public BattleBoardState.ValidatedPoint Tile { get; }

  public HasFactionExploredTile(Faction faction, BattleBoardState.ValidatedPoint tile)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
    Tile = tile;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    return Succeed(session.GetFactionExploredTiles(Faction).Contains(Tile));
  }
}

public sealed class GetVisibleEnemiesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public BattleUnitState ObserverUnit { get; }
  internal int ObserverUnitId => ObserverUnit.Id;

  public GetVisibleEnemiesForUnit(BattleUnitState observerUnit)
  {
    ArgumentNullException.ThrowIfNull(observerUnit);
    ObserverUnit = observerUnit;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    if (!ObserverUnit.IsAlive)
      return FailUnitNotAlive(ObserverUnit);

    return Succeed(ObserverUnit.VisibleUnits
      .Where(unit => unit.Side != ObserverUnit.Side)
      .ToArray());
  }
}

public sealed class GetVisibleUnitsForFaction : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public Faction Faction { get; }

  public GetVisibleUnitsForFaction(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    return Succeed(session.AliveUnits
      .Where(unit => session.IsUnitVisibleToFaction(Faction, unit))
      .ToArray());
  }
}
