using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

// Unit- and object-target attack targeting: candidate tiles are attackable enemies and
// destructible objects under the attacker's current visibility and weapon range, resolved
// through the shared hit-chance query so discovery and preview can never drift; preview is
// the hit-chance breakdown for the entity under the cursor; commit attacks that entity.
// The candidate set may be empty — Attack availability is independent of target discovery,
// so an otherwise usable Attack option stays enabled while targeting finds nothing to hit.
public sealed class AttackTargeting : IActionTargeting
{
  private readonly BattleRuntime _runtime;
  private readonly BattleUnitState _unit;
  private readonly Dictionary<Vector3I, BattleEntity> _targetsByTile = [];

  public AttackTargeting(BattleRuntime runtime, BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(unit);
    _runtime = runtime;
    _unit = unit;
  }

  public ConfirmMode Confirm => ConfirmMode.Immediate;

  public IReadOnlyCollection<Vector3I> Candidates => _targetsByTile.Keys;

  public IReadOnlyCollection<Vector3I> Begin()
  {
    _targetsByTile.Clear();

    return _runtime.TryGetAlive(_unit).Match(
      Some: attacker =>
      {
        void AddCandidate(AliveUnit attacker, BattleEntity entity)
        {
          if (_runtime.TryGetAttackTarget(entity).Case is not AttackTarget target) return;
          if (_runtime.Query(new GetHitChanceForAttack(attacker, target)).IsRight)
            _targetsByTile[target.Position.Raw] = entity;
        }

        foreach (AliveUnit enemy in _runtime.Query(new GetVisibleEnemiesForUnit(attacker)))
          AddCandidate(attacker, new BattleEntity.Unit(enemy.State));

        foreach (BattleObjectState obj in _runtime.Query(new GetBattleSpecialObjectsQuery()))
          AddCandidate(attacker, new BattleEntity.Object(obj));

        return (IReadOnlyCollection<Vector3I>)_targetsByTile.Keys;
      },
      None: () => System.Array.Empty<Vector3I>());
  }

  public Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target)
  {
    if (!_targetsByTile.TryGetValue(target, out BattleEntity? entity))
      return Left<BattleQueryFailure, ActionPreview>(
        new BattleQueryFailure(BattleQueryFailureReason.InvalidTile, $"No attackable target at {target}."));

    return _runtime.TryGetAlive(_unit).Match(
      Some: attacker => _runtime.TryGetAttackTarget(entity).Match(
        Some: targetProof => _runtime.Query(new GetHitChanceForAttack(attacker, targetProof)).Match(
          Right: hit => Right<BattleQueryFailure, ActionPreview>(new AttackPreview(hit)),
          Left: Left<BattleQueryFailure, ActionPreview>),
        None: () => Left<BattleQueryFailure, ActionPreview>(new BattleQueryFailure(
          BattleQueryFailureReason.InvalidBattleState, $"Target at {target} is no longer attackable."))),
      None: () => Left<BattleQueryFailure, ActionPreview>(new BattleQueryFailure(
        BattleQueryFailureReason.InvalidBattleState, "Attacking unit is no longer alive.")));
  }

  public bool CanCommit(Vector3I target) => _targetsByTile.ContainsKey(target);

  public BattleAction Build(Vector3I target)
  {
    // Fresh proofs at the write boundary: the unit may have moved or died, or the object
    // been destroyed, since Begin().
    return _runtime.TryGetAlive(_unit).Match(
      attacker => _runtime.TryGetAttackTarget(_targetsByTile[target]).Match(
        proof => BattleAction.AttackEntity(attacker, proof),
        () => throw new InvalidOperationException("Target is no longer attackable.")),
      () => throw new InvalidOperationException("Attacking unit is no longer alive."));
  }
}
