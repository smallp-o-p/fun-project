using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

// Returns the unit's full POSSIBLE action set (capability-derived), each tagged IsAvailable
// (currently legal + affordable + has a valid target). Cheap: no board BFS; the expensive target
// sets are computed later by the per-verb targeting handlers. Callers cache the result on selection.
public sealed class GetUnitActionOptions : BattleSessionQuery<IReadOnlyList<UnitActionOption>>
{
  private static readonly Vector3I[] OrthogonalNeighbors =
    [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];

  public BattleUnitState Unit { get; }

  public GetUnitActionOptions(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, IReadOnlyList<UnitActionOption>> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    bool inProgress = session.Phase == BattlePhase.InProgress;
    bool canAct = inProgress && session.CanUnitActNow(Unit);
    bool isActiveSide = inProgress && Unit.Side == session.ActiveSide;

    var options = new List<UnitActionOption>
    {
      new MoveActionOption(Unit, canAct && CanMove(session)),
    };

    Unit.EquippedWeapon.IfSome(weapon =>
      options.Add(new AttackActionOption(Unit, weapon, canAct && HasTargetInRange(session, weapon))));

    options.Add(new PassActionOption(Unit, canAct));
    options.Add(new EndTurnActionOption(Unit, isActiveSide));

    return Succeed(options);
  }

  private bool CanMove(BattleSession session)
  {
    if (Unit.CurrentActionPoints < BattleSession.DefaultMovementStepActionPointCost)
      return false;

    return session.GetUnitPosition(Unit).Match(
      Some: pos => OrthogonalNeighbors.Any(dir => session.Board.ValidatePoint(pos.Raw + dir).Match(
        Some: neighbor => session.Board.CanOccupy(neighbor),
        None: () => false)),
      None: () => false);
  }

  private bool HasTargetInRange(BattleSession session, Weapon weapon)
  {
    if (Unit.CurrentActionPoints < BattleSession.DefaultAttackActionPointCost)
      return false;

    int range = weapon.GetRangeStat().BaseValue;
    return session.GetUnitPosition(Unit).Match(
      Some: from => Unit.VisibleUnits.Any(other =>
        other.Side != Unit.Side
        && other.IsAlive
        && session.GetUnitPosition(other).Match(
          Some: targetPos => BattleBoardState.GetGridDistance(from.Raw, targetPos.Raw) <= range,
          None: () => false)),
      None: () => false);
  }
}
