using FunProject.Battle;
using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;

// Enemy-target targeting: candidate tiles are visible enemies within weapon range; preview is the
// hit-chance breakdown for the enemy under the cursor; commit attacks that enemy. Availability of the
// Attack verb (a target exists) and its candidate set are both derived from this same enemy-in-range
// computation, so they cannot disagree.
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

  public IReadOnlyCollection<Vector3I> Candidates => _targetsByTile.Keys;

  public IReadOnlyCollection<Vector3I> Begin()
  {
    _targetsByTile.Clear();

    return _runtime.TryGetAlive(_unit).Match(
      Some: attacker =>
      {
        Vector3I from = attacker.Position.Raw;
        int range = _weapon.EffectiveRange;
        IReadOnlyCollection<AliveUnit> enemies = _runtime.Query(new GetVisibleEnemiesForUnit(attacker));

        foreach (AliveUnit enemy in enemies)
        {
          Vector3I tp = enemy.Position.Raw;
          if (BattleBoardState.GetGridDistance(from, tp) <= range)
            _targetsByTile[tp] = enemy.State;
        }

        return (IReadOnlyCollection<Vector3I>)_targetsByTile.Keys;
      },
      None: () => System.Array.Empty<Vector3I>());
  }

  public Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target)
  {
    if (!_targetsByTile.TryGetValue(target, out BattleUnitState? enemy))
      return Left<BattleQueryFailure, ActionPreview>(
        new BattleQueryFailure(BattleQueryFailureReason.InvalidTile, $"No attackable target at {target}."));

    return _runtime.TryGetAlive(_unit).Match(
      Some: attacker => _runtime.TryGetAlive(enemy).Match(
        Some: enemyProof => _runtime.Query(new GetHitChanceForAttack(attacker, enemyProof)).Match(
          Right: hc => Right<BattleQueryFailure, ActionPreview>(new AttackPreview(hc)),
          Left: Left<BattleQueryFailure, ActionPreview>),
        None: () => Left<BattleQueryFailure, ActionPreview>(
          new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, $"Target at {target} is no longer alive."))),
      None: () => Left<BattleQueryFailure, ActionPreview>(
        new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Attacking unit is no longer alive.")));
  }

  public bool CanCommit(Vector3I target) => _targetsByTile.ContainsKey(target);

  public BattleAction Build(Vector3I target) => BattleAction.AttackUnit(_unit, _targetsByTile[target]);
}
