using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class IsUnitVisibleToUnit(AliveUnit observerUnit, AliveUnit targetUnit) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleReadContext context)
  {
    return ReferenceEquals(observerUnit.State, targetUnit.State) ||
           observerUnit.State.VisibleUnits.Contains(targetUnit.State);
  }
}

public sealed class IsUnitVisibleToFaction(Faction faction, AliveUnit target) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleReadContext context)
  {
    return context.State.IsUnitVisibleToFaction(faction, target.State);
  }
}

public sealed class IsTileVisibleToFaction(Faction faction, BattleBoardState.ValidatedPoint tile) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleReadContext context)
  {
    return context.State.IsTileVisibleToFaction(faction, tile);
  }
}

public sealed class HasFactionExploredTile(Faction faction, BattleBoardState.ValidatedPoint tile) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleReadContext context)
  {
    return context.State.GetFactionExploredTiles(faction).Contains(tile);
  }
}

public sealed class GetVisibleEnemiesForUnit(AliveUnit observer) : IBattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public IReadOnlyCollection<AliveUnit> Execute(BattleReadContext context)
  {
    return observer.State.VisibleUnits
      .AsValueEnumerable().Where(unit => unit.Side != observer.State.Side && unit.IsAlive)
      .Select(context.State.MintAlive)
      .ToArray();
  }
}

public sealed class GetVisibleUnitsForFaction(Faction faction) : IBattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public IReadOnlyCollection<AliveUnit> Execute(BattleReadContext context)
  {
    // Union the already-materialized per-observer visible-unit sets instead of asking
    // IsUnitVisibleToFaction for every unit in the pool (that pass rescanned all observers
    // per unit: O(units x observers)). Each alive faction observer's VisibleUnits already
    // enumerates exactly the units it currently sees.
    var visibleToObservers = new SysColGeneric.HashSet<BattleUnitState>();
    foreach (var observer in context.State.GetFactionAliveUnits(faction))
      visibleToObservers.UnionWith(observer.VisibleUnits);

    // Same result set and ordering as before: iterate AliveUnits in pool order, keep every
    // own-side unit unconditionally (mirrors IsUnitVisibleToFaction's same-side short circuit)
    // plus any other unit seen by some observer. Filtering AliveUnits also drops any stale
    // dead reference exactly as the prior AliveUnits.Where did.
    return context.State.AliveUnits
      .AsValueEnumerable().Where(unit => unit.Side == faction || visibleToObservers.Contains(unit))
      .Select(context.State.MintAlive)
      .ToArray();
  }
}
