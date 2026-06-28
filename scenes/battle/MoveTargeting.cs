using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Tile-path targeting: reachable tiles from GetPossibleMoveTilesForUnit; path preview/commit from
// FindPathForUnit (source-inclusive, so the committed MoveUnit drops index 0). The handler owns the
// reachable set as the single source the controller renders from, and caches the last previewed
// (target, path) so commit reuses the hover preview's path instead of re-querying AStar.
public sealed class MoveTargeting : IActionTargeting
{
  private readonly BattleRuntime _runtime;
  private readonly BattleUnitState _unit;
  private readonly SysColGeneric.HashSet<Vector3I> _reachable = [];
  private Option<(Vector3I Target, Vector3I[] Path)> _lastPath;

  public MoveTargeting(BattleRuntime runtime, BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(unit);
    _runtime = runtime;
    _unit = unit;
  }

  public IReadOnlyCollection<Vector3I> Candidates => _reachable;

  public IReadOnlyCollection<Vector3I> Begin()
  {
    _reachable.Clear();
    _lastPath = None;
    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles =
      _runtime.Query(new GetPossibleMoveTilesForUnit(_unit)).Match(
        Right: r => r,
        Left: _ => System.Array.Empty<BattleBoardState.ValidatedPoint>());
    foreach (BattleBoardState.ValidatedPoint point in tiles)
      _reachable.Add(point.Raw);
    return _reachable;
  }

  public Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target) =>
    ResolvePath(target).Match(
      Right: path =>
      {
        _lastPath = Some((target, path));
        return Right<BattleQueryFailure, ActionPreview>(new PathPreview(path));
      },
      Left: Left<BattleQueryFailure, ActionPreview>);

  public bool CanCommit(Vector3I target) => _reachable.Contains(target);

  public BattleAction Build(Vector3I target)
  {
    // Reuse the path the immediately-preceding hover preview already computed for this target;
    // fall back to a fresh query only if no matching cached path exists.
    Vector3I[] path = _lastPath.Match(
      Some: cached => cached.Target == target ? cached.Path : QueryPath(target),
      None: () => QueryPath(target));
    return BattleAction.MoveUnit(_unit, path.Skip(1));
  }

  private Either<BattleQueryFailure, Vector3I[]> ResolvePath(Vector3I target) =>
    _runtime.Query(new FindPathForUnit(_unit, target)).Match(
      Right: p => Right<BattleQueryFailure, Vector3I[]>(p.Select(point => point.Raw).ToArray()),
      Left: Left<BattleQueryFailure, Vector3I[]>);

  private Vector3I[] QueryPath(Vector3I target) =>
    ResolvePath(target).Match(Right: p => p, Left: _ => System.Array.Empty<Vector3I>());
}
