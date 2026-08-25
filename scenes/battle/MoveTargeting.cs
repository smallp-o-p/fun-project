using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

// Tile-path targeting: reachable tiles from GetPossibleMoveTilesForUnit; path preview/commit from
// FindPathForUnit (source-inclusive, so the committed MoveUnit drops index 0). The handler owns the
// reachable set as the single source the controller renders from, and caches the last previewed
// (target, path) so commit reuses the hover preview's path instead of re-querying the path search.
public sealed class MoveTargeting : IActionTargeting
{
  private readonly BattleRuntime _runtime;
  private readonly BattleUnitState _unit;
  private readonly SysColGeneric.HashSet<Vector3I> _reachable = [];
  private Option<(Vector3I Target, BattleBoardState.ValidatedPoint[] Path)> _lastPath;

  public MoveTargeting(BattleRuntime runtime, BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(unit);
    _runtime = runtime;
    _unit = unit;
  }

  public ConfirmMode Confirm => ConfirmMode.Confirm;

  public IReadOnlyCollection<Vector3I> Candidates => _reachable;

  public IReadOnlyCollection<Vector3I> Begin()
  {
    _reachable.Clear();
    _lastPath = None;
    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles =
      _runtime.TryGetAlive(_unit).Match(
        Some: proof => _runtime.Query(new GetPossibleMoveTilesForUnit(proof)),
        None: () => System.Array.Empty<BattleBoardState.ValidatedPoint>());
    foreach (BattleBoardState.ValidatedPoint point in tiles)
      _reachable.Add(point.Raw);
    return _reachable;
  }

  public Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target) =>
    ResolvePath(target).Match(
      Right: path =>
      {
        _lastPath = Some((target, path));
        return Right<BattleQueryFailure, ActionPreview>(new PathPreview(path.AsValueEnumerable().Select(point => point.Raw).ToArray()));
      },
      Left: Left<BattleQueryFailure, ActionPreview>);

  public bool CanCommit(Vector3I target) => _reachable.Contains(target);

  public BattleAction Build(Vector3I target)
  {
    // Reuse the path the immediately-preceding hover preview already computed for this target;
    // fall back to a fresh query only if no matching cached path exists.
    BattleBoardState.ValidatedPoint[] path = _lastPath.Match(
      Some: cached => cached.Target == target ? cached.Path : QueryPath(target),
      None: () => QueryPath(target));
    // Fresh proof at the write boundary: the mover may have died since Begin().
    return _runtime.TryGetAlive(_unit).Match(
      mover => BattleAction.MoveUnit(mover, path.AsValueEnumerable().Skip(1).ToArray()),
      () => throw new InvalidOperationException("Moving unit is no longer alive."));
  }

  private Either<BattleQueryFailure, BattleBoardState.ValidatedPoint[]> ResolvePath(Vector3I target) =>
    _runtime.TryGetTile(target).Match(
      Some: targetPoint => _runtime.TryGetAlive(_unit).Match(
        Some: proof => _runtime.Query(new FindPathForUnit(proof, targetPoint)),
        None: () => Left<BattleQueryFailure, BattleBoardState.ValidatedPoint[]>(
          new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Moving unit is no longer alive."))),
      None: () => Left<BattleQueryFailure, BattleBoardState.ValidatedPoint[]>(
        new BattleQueryFailure(BattleQueryFailureReason.InvalidTile, $"{target} is outside the battle board.")));

  private BattleBoardState.ValidatedPoint[] QueryPath(Vector3I target) =>
    ResolvePath(target).Match(Right: p => p, Left: _ => System.Array.Empty<BattleBoardState.ValidatedPoint>());
}
