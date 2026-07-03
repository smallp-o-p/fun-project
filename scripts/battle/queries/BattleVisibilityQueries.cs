using FunProject.Combatants;
using System;
using System.Collections.Generic;
using System.Linq;
namespace FunProject.Battle;

public sealed class IsUnitVisibleToUnit : BattleSessionQuery<bool>
{
  public AliveUnit ObserverUnit { get; }
  public AliveUnit TargetUnit { get; }

  public IsUnitVisibleToUnit(AliveUnit observerUnit, AliveUnit targetUnit)
  {
    ObserverUnit = observerUnit;
    TargetUnit = targetUnit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    if (ReferenceEquals(ObserverUnit.State, TargetUnit.State))
      return Succeed(true);

    return Succeed(ObserverUnit.State.VisibleUnits.Contains(TargetUnit.State));
  }
}

public sealed class IsUnitVisibleToFaction : BattleSessionQuery<bool>
{
  public Faction Faction { get; }
  public AliveUnit TargetUnit { get; }

  public IsUnitVisibleToFaction(Faction faction, AliveUnit targetUnit)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
    TargetUnit = targetUnit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    return Succeed(session.IsUnitVisibleToFaction(Faction, TargetUnit.State));
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

public sealed class GetVisibleEnemiesForUnit : BattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public AliveUnit ObserverUnit { get; }

  public GetVisibleEnemiesForUnit(AliveUnit observerUnit)
  {
    ObserverUnit = observerUnit;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<AliveUnit>> Execute(BattleSession session)
  {
    // VisibleUnits only ever holds alive, in-session units, so each is safe to mint.
    return Succeed(ObserverUnit.State.VisibleUnits
      .Where(unit => unit.Side != ObserverUnit.State.Side)
      .Select(session.MintAlive)
      .ToArray());
  }
}

public sealed class GetVisibleUnitsForFaction : BattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public Faction Faction { get; }

  public GetVisibleUnitsForFaction(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<AliveUnit>> Execute(BattleSession session)
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
      .Select(session.MintAlive)
      .ToArray());
  }
}
