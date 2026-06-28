using FunProject.Weapons;
using Godot;
using System;
using System.Linq;

namespace FunProject.Battle;

// Cheap, no-BFS availability fact for a unit's verbs: legal + affordable + (for Attack) a target in
// range. Pure domain value — carries NO presentation taxonomy. Presentation maps it to concrete verb
// options/targeting strategies. The expensive candidate sets (reachable tiles, attackable enemies) are
// computed later by the per-verb targeting handlers on the presentation side.
public sealed record UnitActionAvailability(
  bool CanMove,
  bool CanAttack,
  bool CanPass,
  bool CanEndTurn);

// Reports the acting unit's verb availability as a domain fact. Cheap: no board BFS; uses only
// session-internal members (CanUnitActNow, AP costs, board occupancy, visible-enemies). Callers cache
// the result on selection.
public sealed class GetUnitActionOptions : BattleSessionQuery<UnitActionAvailability>
{
  private static readonly Vector3I[] OrthogonalNeighbors =
    [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];

  public BattleUnitState Unit { get; }

  public GetUnitActionOptions(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, UnitActionAvailability> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    bool inProgress = session.Phase == BattlePhase.InProgress;
    bool canAct = inProgress && session.CanUnitActNow(Unit);
    bool isActiveSide = inProgress && Unit.Side == session.ActiveSide;

    bool canMove = canAct && CanMove(session);
    bool canAttack = canAct && Unit.EquippedWeapon.Match(
      Some: weapon => HasTargetInRange(session, weapon),
      None: () => false);

    return Succeed(new UnitActionAvailability(canMove, canAttack, canAct, isActiveSide));
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

    int range = weapon.EffectiveRange;
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
