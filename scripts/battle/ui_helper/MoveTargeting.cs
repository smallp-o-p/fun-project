using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;
// Tile-path targeting: reachable tiles from GetPossibleMoveTilesForUnit; path preview/commit from
// FindPathForUnit (source-inclusive, so the committed MoveUnit drops index 0).
public sealed class MoveTargeting : IActionTargeting
{
  private readonly BattleRuntime _runtime;
  private readonly BattleUnitState _unit;
  private readonly SysColGeneric.HashSet<Vector3I> _reachable = [];

  public MoveTargeting(BattleRuntime runtime, BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(unit);
    _runtime = runtime;
    _unit = unit;
  }

  public TargetingKind Kind => TargetingKind.TilePath;

  public IReadOnlyCollection<Vector3I> Begin()
  {
    _reachable.Clear();
    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles =
      _runtime.Query(new GetPossibleMoveTilesForUnit(_unit)).Match(
        Right: r => r,
        Left: _ => System.Array.Empty<BattleBoardState.ValidatedPoint>());
    foreach (BattleBoardState.ValidatedPoint point in tiles)
      _reachable.Add(point.Raw);
    return _reachable;
  }

  public Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target) =>
    _runtime.Query(new FindPathForUnit(_unit, target)).Match(
      Right: path => Right<BattleQueryFailure, ActionPreview>(
        new ActionPreview(TargetingKind.TilePath, [.. path.Select(p => p.Raw)], null, null)),
      Left: Left<BattleQueryFailure, ActionPreview>);

  public bool CanCommit(Vector3I target) => _reachable.Contains(target);

  public BattleAction Build(Vector3I target)
  {
    Vector3I[] path = _runtime.Query(new FindPathForUnit(_unit, target)).Match(
      Right: p => p.Select(point => point.Raw).ToArray(),
      Left: _ => System.Array.Empty<Vector3I>());
    return BattleAction.MoveUnit(_unit, path.Skip(1));
  }
}
