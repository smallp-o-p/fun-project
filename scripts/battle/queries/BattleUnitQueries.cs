using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class GetUnitPosition : BattleSessionQuery<BattleBoardState.ValidatedPoint>
{
  public BattleUnitState Unit { get; }

  public GetUnitPosition(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint> Execute(BattleSession session)
  {
    return session.GetUnitPosition(Unit).Match(
      Succeed,
      () => Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Unit.Id} is not on the board."));
  }
}

public sealed class GetUnitAtTile : BattleSessionQuery<Option<BattleUnitState>>
{
  public Vector3I Coordinates { get; }

  public GetUnitAtTile(Vector3I coordinates)
  {
    Coordinates = coordinates;
  }

  internal override Either<BattleQueryFailure, Option<BattleUnitState>> Execute(BattleSession session)
  {
    return session.Board.ValidatePoint(Coordinates).Match(
      point => Succeed(session.GetUnitAt(point)),
      () => Fail(BattleQueryFailureReason.InvalidTile, $"{Coordinates} is invalid"));
  }
}

public sealed class GetFactionAliveUnits : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public Faction Side { get; }

  public GetFactionAliveUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    return Succeed(session.GetFactionAliveUnits(Side).ToArray());
  }
}

public sealed class GetFactionDeadUnits : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public Faction Side { get; }

  public GetFactionDeadUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    return Succeed(session.DeadUnits.Where(unit => unit.Side == Side).ToArray());
  }
}

public sealed class CanUnitActNow : BattleSessionQuery<bool>
{
  public BattleUnitState Unit { get; }
  public CanUnitActNow(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query active unit state while the battle is not in progress.");
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    return Succeed(session.CanUnitActNow(Unit));
  }
}

public sealed class IsUnitStillAvailableThisTurn : BattleSessionQuery<bool>
{
  public BattleUnitState Unit { get; }

  public IsUnitStillAvailableThisTurn(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query unit turn availability while the battle is not in progress.");
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    return Succeed(session.IsUnitStillAvailableThisTurn(Unit));
  }
}
