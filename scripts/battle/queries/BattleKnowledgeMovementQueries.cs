using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

public enum TraversalKnowledge { Blocked, Clear, Unknown }
public readonly record struct TileKnowledge(Option<BattleTileState> Terrain, TraversalKnowledge Occupancy);

public sealed class GetTileKnowledgeForFaction(Faction faction, BattleBoardState.ValidatedPoint tile) : IBattleSessionQuery<TileKnowledge>
{
  public TileKnowledge Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(faction);
    var terrain = session.ObservationHistory.GetTerrain(faction, tile);
    // Current vision admits occupancy. Outside it only an own-side unit is known;
    // neither hidden blockers nor remembered contacts may influence an estimate.
    TraversalKnowledge occupancy;
    if (session.IsTileVisibleToFaction(faction, tile))
      occupancy = session.Board.IsOccupied(tile) || session.Board.IsBlockedByObject(tile)
        ? TraversalKnowledge.Blocked : TraversalKnowledge.Clear;
    else if (session.GetFactionAliveUnits(faction).AsValueEnumerable().Any(unit => session.GetUnitPosition(unit).Contains(tile)))
      occupancy = TraversalKnowledge.Blocked;
    else
      occupancy = TraversalKnowledge.Unknown;
    return new(terrain, occupancy);
  }

  internal static TraversalKnowledge Traversal(BattleSession session, Faction faction, BattleBoardState.ValidatedPoint tile)
  {
    var known = new GetTileKnowledgeForFaction(faction, tile).Execute(session);
    if (known.Terrain.Match(value => !value.IsWalkable, () => false) || known.Occupancy == TraversalKnowledge.Blocked)
      return TraversalKnowledge.Blocked;
    return known.Terrain.IsSome && known.Occupancy == TraversalKnowledge.Clear ? TraversalKnowledge.Clear : TraversalKnowledge.Unknown;
  }

}

public sealed class FindKnownPathForUnit(Faction faction, AliveUnit unit, BattleBoardState.ValidatedPoint destination) : IBattleSessionQuery<Vector3I[]>
{
  public Vector3I[] Execute(BattleSession session)
  {
    var path = BattlePathSearch.FindPath(session.Board.Dimensions, unit.Position, destination,
      tile => GetTileKnowledgeForFaction.Traversal(session, faction, tile));
    return path.AsValueEnumerable().Select(point => point.Raw).ToArray();
  }
}

public sealed class GetKnownMoveTilesForUnit(Faction faction, AliveUnit unit, int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost) : IBattleSessionQuery<IReadOnlyList<Vector3I>>
{
  public IReadOnlyList<Vector3I> Execute(BattleSession session)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);
    if (unit.State.IsIncapacitated || unit.State.CurrentActionPoints < actionPointCostPerStep) return [];
    int steps = actionPointCostPerStep == 0 ? int.MaxValue : unit.State.CurrentActionPoints / actionPointCostPerStep;
    return System.Array.AsReadOnly(BattlePathSearch.GetReachableTiles(session.Board.Dimensions, unit.Position, steps,
      tile => GetTileKnowledgeForFaction.Traversal(session, faction, tile)).AsValueEnumerable()
      .Select(tile => tile.Raw).ToArray());
  }
}
