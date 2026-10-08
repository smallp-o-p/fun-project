using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

public enum TraversalKnowledge { Blocked, Clear, Unknown }
public readonly record struct TileKnowledge(Option<BattleTileState> Terrain, TraversalKnowledge Occupancy);
public readonly record struct KnownPath(Vector3I[] Points, bool IsEstimated);
public readonly record struct KnownReachableTile(Vector3I Position, bool IsEstimated);

public sealed class GetTileKnowledgeForFaction(Faction faction, BattleBoardState.ValidatedPoint tile) : IBattleSessionQuery<TileKnowledge>
{
  public TileKnowledge Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(faction);
    var terrain = session.ObservationHistory.GetTerrain(faction, tile);
    // Current vision admits occupancy. Outside it only an own-side unit is known;
    // neither hidden blockers nor remembered contacts may influence an estimate.
    var occupancy = session.IsTileVisibleToFaction(faction, tile)
      ? (session.Board.IsOccupied(tile) || session.Board.IsBlockedByObject(tile) ? TraversalKnowledge.Blocked : TraversalKnowledge.Clear)
      : session.GetFactionAliveUnits(faction).AsValueEnumerable().Any(unit => session.GetUnitPosition(unit).Contains(tile))
        ? TraversalKnowledge.Blocked : TraversalKnowledge.Unknown;
    return new(terrain, occupancy);
  }

  internal static TraversalKnowledge Traversal(BattleSession session, Faction faction, BattleBoardState.ValidatedPoint tile)
  {
    var known = new GetTileKnowledgeForFaction(faction, tile).Execute(session);
    if (known.Terrain.Match(value => !value.IsWalkable, () => false) || known.Occupancy == TraversalKnowledge.Blocked)
      return TraversalKnowledge.Blocked;
    return known.Terrain.IsSome && known.Occupancy == TraversalKnowledge.Clear ? TraversalKnowledge.Clear : TraversalKnowledge.Unknown;
  }

  internal static void RequireActor(BattleSession session, Faction faction, AliveUnit unit)
  {
    ArgumentNullException.ThrowIfNull(faction);
    if (unit.State.Side != faction || session.TryGetAlive(unit.State).IsNone)
      throw new ArgumentException("Knowledge movement requires a living actor belonging to this session and faction.", nameof(unit));
  }
}

public sealed class FindKnownPathForUnit(Faction faction, AliveUnit unit, BattleBoardState.ValidatedPoint destination) : IBattleSessionQuery<KnownPath>
{
  public KnownPath Execute(BattleSession session)
  {
    GetTileKnowledgeForFaction.RequireActor(session, faction, unit);
    var path = BattlePathSearch.FindPath(session.Board.Dimensions, unit.Position, destination,
      tile => GetTileKnowledgeForFaction.Traversal(session, faction, tile));
    return new(path.Points.AsValueEnumerable().Select(point => point.Raw).ToArray(), path.IsEstimated);
  }
}

public sealed class GetKnownMoveTilesForUnit(Faction faction, AliveUnit unit, int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost) : IBattleSessionQuery<IReadOnlyList<KnownReachableTile>>
{
  public IReadOnlyList<KnownReachableTile> Execute(BattleSession session)
  {
    GetTileKnowledgeForFaction.RequireActor(session, faction, unit);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);
    if (unit.State.IsIncapacitated || unit.State.CurrentActionPoints < actionPointCostPerStep) return [];
    int steps = actionPointCostPerStep == 0 ? int.MaxValue : unit.State.CurrentActionPoints / actionPointCostPerStep;
    return System.Array.AsReadOnly(BattlePathSearch.GetReachableTiles(session.Board.Dimensions, unit.Position, steps,
      tile => GetTileKnowledgeForFaction.Traversal(session, faction, tile)).AsValueEnumerable()
      .Select(tile => new KnownReachableTile(tile.Point.Raw, tile.IsEstimated)).ToArray());
  }
}
