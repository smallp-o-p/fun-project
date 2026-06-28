using FunProject.Combatants;
using System;
using System.Collections.Generic;
using System.Linq;
namespace FunProject.Battle;

public sealed class IsUnitVisibleToUnit : BattleSessionQuery<bool>
{
  public BattleUnitState ObserverUnit { get; }
  public BattleUnitState TargetUnit { get; }

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
    if (ReferenceEquals(ObserverUnit, TargetUnit))
      return Succeed(true);

    return Succeed(ObserverUnit.VisibleUnits.Contains(TargetUnit));
  }
}

public sealed class IsUnitVisibleToFaction : BattleSessionQuery<bool>
{
  public Faction Faction { get; }
  public BattleUnitState TargetUnit { get; }

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
    // Union the already-materialized per-observer visible-unit sets instead of asking
    // IsUnitVisibleToFaction for every unit in the pool (that pass rescanned all observers
    // per unit: O(units x observers)). Each alive faction observer's VisibleUnits already
    // enumerates exactly the units it currently sees.
    var visibleToObservers = new SysColGeneric.HashSet<BattleUnitState>();
    foreach (var observer in session.GetFactionAliveUnits(Faction))
      visibleToObservers.UnionWith(observer.VisibleUnits);

    // Same result set and ordering as before: iterate AliveUnits in pool order, keep every
    // own-side unit unconditionally (mirrors IsUnitVisibleToFaction's same-side short circuit)
    // plus any other unit seen by some observer. Filtering AliveUnits also drops any stale
    // dead reference exactly as the prior AliveUnits.Where did.
    return Succeed(session.AliveUnits
      .Where(unit => unit.Side == Faction || visibleToObservers.Contains(unit))
      .ToArray());
  }
}
