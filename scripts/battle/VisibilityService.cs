using FunProject.Combatants;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

internal sealed class VisibilityService
{
  private static readonly IReadOnlySet<BattleBoardState.ValidatedPoint> EmptyTileSet =
    new SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>();

  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>>
    _exploredTilesByFaction = [];

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetExploredTiles(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _exploredTilesByFaction.TryGetValue(side, out var tiles) ? tiles : EmptyTileSet;
  }

  internal IReadOnlyList<(BattleUnitState Observer, BattleUnitState Target)> RefreshAllUnits(
    BattleBoardState board,
    IReadOnlyList<BattleUnitState> allUnits,
    IEnumerable<BattleUnitState> aliveUnits)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(allUnits);
    ArgumentNullException.ThrowIfNull(aliveUnits);

    // Include unconscious living units so the full pass clears their stale caches too.
    var alive = new SysColGeneric.HashSet<BattleUnitState>(aliveUnits);
    return RefreshAffected(board, allUnits, alive);
  }

  // Incremental counterpart to a full recompute (RefreshAllUnits), applied after an occupancy
  // or consciousness change (move/spawn/death/knockout). Line-of-sight blocking is a tile
  // property and the position/vision of every UNaffected observer is unchanged, so their
  // visible-TILE sets are invariant under another unit moving, spawning, or dying. Therefore:
  //   (1) recompute each affected unit's own visible tiles (and union its newly seen tiles
  //       into its faction's explored memory, exactly as a full pass would), then
  //   (2) rebuild every conscious observer's visible-UNIT membership wholesale from its
  //       visible tiles — the same rule a full pass applies — instead of surgically flipping
  //       individual memberships. With battle-scale unit counts the O(observers × units) set
  //       lookups are trivial and the membership derivation becomes one uniform code path.
  // The resulting per-unit sets and per-faction explored memory are identical to recomputing
  // every alive unit from scratch (which RefreshAllUnits does by calling this with
  // affected == every alive unit). First-time spottings are detected directly through
  // RecordFirstSpotting's lifetime memory: a currently visible pair that was never recorded
  // cannot have been visible at any earlier refresh (it would have been recorded then), so no
  // prior-set snapshot is needed to detect novelty.
  internal IReadOnlyList<(BattleUnitState Observer, BattleUnitState Target)> RefreshAffected(
    BattleBoardState board,
    IReadOnlyList<BattleUnitState> allUnits,
    IReadOnlySet<BattleUnitState> affectedUnits)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(allUnits);
    ArgumentNullException.ThrowIfNull(affectedUnits);

    var spotted = new List<(BattleUnitState, BattleUnitState)>();

    // (1) Recompute visible tiles for affected observers only. A living unit is always
    // indexed on the board (Refresh enforces the same invariant); dead and unconscious units
    // see nothing, so their just-cleared sets are correct.
    foreach (var affected in affectedUnits)
    {
      affected.ClearVisibility();
      if (affected.IsAlive && !affected.IsUnconscious)
        RecomputeObserver(board, affected);
    }

    // (2) Rebuild visible-UNIT membership for every conscious observer from its visible
    // tiles (freshly recomputed for affected observers, invariant for the rest): a target is
    // visible iff it is alive and stands on a visible tile.
    var positionedTargets =
      new List<(BattleUnitState Target, BattleBoardState.ValidatedPoint Position)>();
    foreach (var candidate in allUnits)
    {
      if (!candidate.IsAlive)
        continue;
      var position = board.FindOccupantPosition(candidate.Id);
      if (position.IsSome)
        positionedTargets.Add((candidate, position.Value()));
    }

    foreach (var observer in allUnits)
    {
      if (!observer.IsAlive || observer.IsUnconscious)
        continue;

      // Replace, not merge: membership must reflect exactly the targets currently standing on
      // visible tiles, so a target that moved out of sight loses its stale membership even
      // though this observer's visible-tile set itself was never recomputed.
      observer.ClearVisibleUnits();
      foreach (var (target, position) in positionedTargets)
        if (!ReferenceEquals(target, observer) && observer.VisibleTiles.Contains(position))
          observer.AddVisibleUnit(target);

      foreach (var target in observer.VisibleUnits)
        if (observer.RecordFirstSpotting(target))
          spotted.Add((observer, target));
    }

    spotted.Sort(CompareSpottedDelta);
    return spotted;
  }

  private void RecomputeObserver(BattleBoardState board, BattleUnitState observer)
  {
    var observerPositionOption = board.FindOccupantPosition(observer.Id);
    if (observerPositionOption.IsNone)
      throw new InvalidOperationException(
        $"Living unit {observer.Id} is missing from the session position index.");
    var observerPosition = observerPositionOption.Value();

    var observerVisibleTiles = GetVisibleTiles(board, observer, observerPosition);
    foreach (var visibleTile in observerVisibleTiles)
      observer.AddVisibleTile(visibleTile);

    MarkTilesExplored(observer.Side, observerVisibleTiles);
  }

  private void MarkTilesExplored(Faction side, IEnumerable<BattleBoardState.ValidatedPoint> tiles)
  {
    ArgumentNullException.ThrowIfNull(side);
    ArgumentNullException.ThrowIfNull(tiles);

    if (!_exploredTilesByFaction.TryGetValue(side, out var exploredTiles))
    {
      exploredTiles = [];
      _exploredTilesByFaction.Add(side, exploredTiles);
    }

    exploredTiles.UnionWith(tiles);
  }

  // Newly-spotted pairs are enqueued into the committed event stream, so their order must be
  // deterministic — reference-keyed Dictionary/HashSet iteration is not. Order by observer id
  // then target id (matches the seedable-RNG determinism discipline elsewhere in the runtime).
  private static int CompareSpottedDelta(
    (BattleUnitState Observer, BattleUnitState Target) a,
    (BattleUnitState Observer, BattleUnitState Target) b)
  {
    int byObserver = a.Observer.Id.CompareTo(b.Observer.Id);
    return byObserver != 0 ? byObserver : a.Target.Id.CompareTo(b.Target.Id);
  }

  // Computes the set of tiles visible from observerPosition using a 3D Euclidean range check
  // over a bounding-box scan, with a per-candidate LOS raytrace.
  private SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> GetVisibleTiles(
    BattleBoardState board,
    BattleUnitState observer,
    BattleBoardState.ValidatedPoint observerPosition)
  {
    ArgumentNullException.ThrowIfNull(observer);

    var visibleTiles = new SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>();
    visibleTiles.Add(observerPosition);

    int vision = observer.Vision;
    if (vision < 0)
      return visibleTiles;

    int visionSq = vision * vision;
    Vector3I pos = observerPosition.Raw;

    // Iterate the bounding box [pos-vision, pos+vision] clamped to the board.
    for (int dy = -vision; dy <= vision; dy++)
      for (int dz = -vision; dz <= vision; dz++)
        for (int dx = -vision; dx <= vision; dx++)
        {
          if (dx == 0 && dy == 0 && dz == 0)
            continue;

          // 3D Euclidean distance check (includes Y).
          if (dx * dx + dy * dy + dz * dz > visionSq)
            continue;

          var candidateOption = board.ValidatePoint(new Vector3I(pos.X + dx, pos.Y + dy, pos.Z + dz));
          if (candidateOption.IsNone)
            continue;
          var candidate = candidateOption.Value();

          if (IsLineOfSightClear(board, observerPosition, candidate))
            visibleTiles.Add(candidate);
        }

    return visibleTiles;
  }

  // Orders two axes' rational crossing times a=aNum/aDen and b=bNum/bDen (denominators > 0):
  // returns sign(a − b). A long.MaxValue numerator represents +∞ (that axis never crosses).
  private static int CompareCrossingTime(long aNum, long aDen, long bNum, long bDen)
  {
    if (aNum == long.MaxValue)
      return bNum == long.MaxValue ? 0 : 1;
    if (bNum == long.MaxValue)
      return -1;
    return (aNum * bDen).CompareTo(bNum * aDen);
  }

  // Deterministic integer 3D supercover voxel traversal (Amanatides–Woo style).
  // Crossing times are represented as exact rationals (numerator / denominator) so there is no
  // floating-point jitter. Three blocking rules are applied at each traversal step:
  //   1. Solid intermediate cell: any strictly-intermediate cell with BlocksLineOfSight blocks.
  //   2. Horizontal diagonal corner: when X and Z both cross at the same Y, the passage is sealed
  //      iff BOTH horizontally-flanking cells block.
  //   3. Vertical floor/ceiling: when Y changes, the upper cell's BlocksVerticalLineOfSight seals
  //      the passage (applies to ALL steps, including those adjacent to endpoints).
  // Precondition: `from` and `to` are validated (in-bounds) points. The walk moves each coordinate
  // monotonically from `from` toward `to`, so every cell it touches — intermediate cells, both
  // diagonal flanks, and the vertical upper/lower cell — lies within their bounding box and is in
  // bounds. Tiles are therefore read via GetTileUnchecked with no per-step bounds validation.
  private bool IsLineOfSightClear(
    BattleBoardState board,
    BattleBoardState.ValidatedPoint from,
    BattleBoardState.ValidatedPoint to)
  {
    if (from == to)
      return true;

    int cx = from.X, cy = from.Y, cz = from.Z;
    int ex = to.X, ey = to.Y, ez = to.Z;

    int signX = Math.Sign(ex - cx);
    int signY = Math.Sign(ey - cy);
    int signZ = Math.Sign(ez - cz);
    int adx = Math.Abs(ex - cx);
    int ady = Math.Abs(ey - cy);
    int adz = Math.Abs(ez - cz);

    // Each axis's crossing times are the rational sequence (2k+1)/(2·aDelta) for k=0,1,…
    // Represent as numerator and denominator; long.MaxValue numerator = axis never crosses.
    // Safe within game-scale boards: adx,ady,adz ≤ ~200, so products fit in long easily.
    long tNumX = adx > 0 ? 1L : long.MaxValue;
    long tNumY = ady > 0 ? 1L : long.MaxValue;
    long tNumZ = adz > 0 ? 1L : long.MaxValue;
    long tDenX = adx > 0 ? 2L * adx : 1L;
    long tDenY = ady > 0 ? 2L * ady : 1L;
    long tDenZ = adz > 0 ? 2L * adz : 1L;

    while (cx != ex || cy != ey || cz != ez)
    {
      // Cross the axis (or axes, on a tie) with the minimum crossing time. Ties advance multiple
      // axes in one iteration (a diagonal step), which Rules 2 and 3 resolve for occlusion.
      int cmpXY = CompareCrossingTime(tNumX, tDenX, tNumY, tDenY);
      int cmpXZ = CompareCrossingTime(tNumX, tDenX, tNumZ, tDenZ);
      int cmpYZ = CompareCrossingTime(tNumY, tDenY, tNumZ, tDenZ);

      bool crossX = cmpXY <= 0 && cmpXZ <= 0;
      bool crossY = cmpXY >= 0 && cmpYZ <= 0;
      bool crossZ = cmpXZ >= 0 && cmpYZ >= 0;

      int prevX = cx, prevY = cy, prevZ = cz;

      if (crossX) { cx += signX; tNumX += 2; }
      if (crossY) { cy += signY; tNumY += 2; }
      if (crossZ) { cz += signZ; tNumZ += 2; }

      bool isEndpoint = cx == ex && cy == ey && cz == ez;

      // Rule 1: strictly-intermediate cells with BlocksLineOfSight stop sight.
      // Endpoints (from and to) are exempt.
      if (!isEndpoint && board.GetTileUnchecked(cx, cy, cz).BlocksLineOfSight)
        return false;

      // Rule 2: horizontal diagonal corner (X and Z both cross, Y unchanged). Passage is sealed
      // iff BOTH flanking cells block. Each flank shares one coordinate with the previous cell and
      // one with the current cell, so both are in bounds.
      if (crossX && crossZ && !crossY
          && board.GetTileUnchecked(prevX + signX, prevY, prevZ).BlocksLineOfSight
          && board.GetTileUnchecked(prevX, prevY, prevZ + signZ).BlocksLineOfSight)
        return false;

      // Rule 3: vertical floor/ceiling — applies to every step where Y changes, including those
      // adjacent to endpoints. The floor belongs to the upper cell (larger Y); both candidate
      // cells (prev and current) are visited, so both are in bounds.
      if (crossY)
      {
        (int ux, int uy, int uz) = prevY > cy ? (prevX, prevY, prevZ) : (cx, cy, cz);
        if (board.GetTileUnchecked(ux, uy, uz).BlocksVerticalLineOfSight)
          return false;
      }
    }

    return true;
  }
}
