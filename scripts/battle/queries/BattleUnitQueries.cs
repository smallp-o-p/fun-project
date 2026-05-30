using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class GetUnitPosition : BattleSessionQuery<BattleBoardState.ValidatedPoint>
{
  public const string Id = "get_unit_position";

  public BattleUnitState Unit { get; }

  public GetUnitPosition(BattleUnitState unit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return session.GetUnitPosition(Unit).Match(
      Succeed,
      () => Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Unit.Id} is not on the board."));
  }
}

public sealed class GetUnitAtTile : BattleSessionQuery<Option<BattleUnitState>>
{
  public const string Id = "get_unit_at_tile";

  public Vector3I Coordinates { get; }

  public GetUnitAtTile(Vector3I coordinates)
    : base(Id)
  {
    Coordinates = coordinates;
  }

  internal override Either<BattleQueryFailure, Option<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return session.Board.ValidatePoint(Coordinates).Match(
      point => Succeed(session.GetUnitAt(point)),
      () => Fail(BattleQueryFailureReason.InvalidTile, $"{Coordinates} is invalid"));
  }
}

public sealed class GetFactionAliveUnits : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_faction_alive_units";

  public Faction Side { get; }

  public GetFactionAliveUnits(Faction side)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.GetFactionAliveUnits(Side).ToArray());
  }
}

public sealed class GetFactionDeadUnits : BattleSessionQuery<IEnumerable<BattleUnitState>>
{
  public const string Id = "get_faction_dead_units";

  public Faction Side { get; }

  public GetFactionDeadUnits(Faction side)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, IEnumerable<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.DeadUnits);
  }
}

public sealed class CanUnitActNow : BattleSessionQuery<bool>
{
  public const string Id = "can_unit_act_now";

  public BattleUnitState Unit { get; }
  public CanUnitActNow(BattleUnitState unit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query active unit state while the battle is not in progress.");
    if (!Unit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {Unit.Id} is not alive.");

    return Succeed(session.CanUnitActNow(Unit));
  }
}

public sealed class IsUnitStillAvailableThisTurn : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_still_available_this_turn";

  public BattleUnitState Unit { get; }

  public IsUnitStillAvailableThisTurn(BattleUnitState unit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query unit turn availability while the battle is not in progress.");
    if (!Unit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {Unit.Id} is not alive.");

    return Succeed(session.IsUnitStillAvailableThisTurn(Unit));
  }
}

public sealed class CanOccupyTile : BattleSessionQuery<bool>
{
  public const string Id = "can_occupy_tile";

  public Vector3I Coordinates { get; }

  public CanOccupyTile(Vector3I coordinates)
    : base(Id)
  {
    Coordinates = coordinates;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return session.Board.ValidatePoint(Coordinates).Match(
      point => Succeed(session.Board.CanOccupy(point)),
      () => Succeed(false));
  }
}
