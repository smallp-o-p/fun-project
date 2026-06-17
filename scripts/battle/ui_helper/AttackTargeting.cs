using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

// Enemy-target targeting: candidate tiles are visible enemies within weapon range; preview is the
// hit-chance breakdown for the enemy under the cursor; commit attacks that enemy.
public sealed class AttackTargeting : IActionTargeting
{
  private readonly BattleRuntime _runtime;
  private readonly BattleUnitState _unit;
  private readonly Weapon _weapon;
  private readonly Dictionary<Vector3I, BattleUnitState> _targetsByTile = [];

  public AttackTargeting(BattleRuntime runtime, BattleUnitState unit, Weapon weapon)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(weapon);
    _runtime = runtime;
    _unit = unit;
    _weapon = weapon;
  }

  public TargetingKind Kind => TargetingKind.EnemyTarget;

  public IReadOnlyCollection<Vector3I> Begin()
  {
    _targetsByTile.Clear();

    Vector3I? from = _runtime.Query(new GetUnitPosition(_unit)).Match(
      Right: p => (Vector3I?)p.Raw, Left: _ => null);
    if (from is null)
      return System.Array.Empty<Vector3I>();

    int range = _weapon.GetRangeStat().BaseValue;
    IReadOnlyCollection<BattleUnitState> enemies = _runtime.Query(new GetVisibleEnemiesForUnit(_unit)).Match(
      Right: e => e, Left: _ => System.Array.Empty<BattleUnitState>());

    foreach (BattleUnitState enemy in enemies)
    {
      Vector3I? targetPos = _runtime.Query(new GetUnitPosition(enemy)).Match(
        Right: p => (Vector3I?)p.Raw, Left: _ => null);
      if (targetPos is Vector3I tp && BattleBoardState.GetGridDistance(from.Value, tp) <= range)
        _targetsByTile[tp] = enemy;
    }

    return _targetsByTile.Keys.ToArray();
  }

  public Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target)
  {
    if (!_targetsByTile.TryGetValue(target, out BattleUnitState? enemy))
      return Left<BattleQueryFailure, ActionPreview>(
        new BattleQueryFailure(BattleQueryFailureReason.InvalidTile, $"No attackable target at {target}."));

    return _runtime.Query(new GetHitChanceForAttack(_unit, enemy)).Match(
      Right: hc => Right<BattleQueryFailure, ActionPreview>(
        new ActionPreview(TargetingKind.EnemyTarget, [], hc, target)),
      Left: Left<BattleQueryFailure, ActionPreview>);
  }

  public bool CanCommit(Vector3I target) => _targetsByTile.ContainsKey(target);

  public BattleAction Build(Vector3I target) => BattleAction.AttackUnit(_unit, _targetsByTile[target]);
}
