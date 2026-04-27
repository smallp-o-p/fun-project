using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class GetUnit : BattleSessionQuery<BattleUnitState>
{
  public const string Id = "get_unit";

  public BattleSession.BattleUnitHandle UnitHandle { get; }

  public GetUnit(BattleSession.BattleUnitHandle unitHandle)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    UnitHandle = unitHandle;
  }

  internal override BattleQueryResult<BattleUnitState> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return session.GetUnit(UnitHandle).Match(
      Succeed,
      () => Fail(BattleQueryFailureReason.UnknownUnit, $"Unknown unit id {UnitHandle.UnitId}."));
  }
}

public sealed class GetLivingUnit : BattleSessionQuery<BattleUnitState>
{
  public const string Id = "get_living_unit";

  public BattleSession.BattleUnitHandle UnitHandle { get; }

  public GetLivingUnit(BattleSession.BattleUnitHandle unitHandle)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    UnitHandle = unitHandle;
  }

  internal override BattleQueryResult<BattleUnitState> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return session.GetLivingUnit(UnitHandle).Match(
      Succeed,
      () => session.GetUnit(UnitHandle).Match(
        _ => Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {UnitHandle.UnitId} is not alive."),
        () => Fail(BattleQueryFailureReason.UnknownUnit, $"Unknown unit id {UnitHandle.UnitId}.")));
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

  internal override BattleQueryResult<IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.GetFactionAliveUnits(Side).ToArray());
  }
}

public sealed class GetFactionDeadUnits : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_faction_dead_units";

  public Faction Side { get; }

  public GetFactionDeadUnits(Faction side)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override BattleQueryResult<IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.DeadUnits);
  }
}

public sealed class CanUnitActNow : BattleSessionQuery<bool>
{
  public const string Id = "can_unit_act_now";

  public BattleSession.BattleUnitHandle UnitHandle { get; }
  public CanUnitActNow(BattleSession.BattleUnitHandle unitHandle)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    UnitHandle = unitHandle;
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query active unit state while the battle is not in progress.");

    BattleQueryResult<BattleUnitState> unitResult = new GetLivingUnit(UnitHandle).Execute(session);
    if (unitResult is BattleQueryFailureResult<BattleUnitState> failureResult)
      return Fail(failureResult.Failure.Reason, failureResult.Failure.Message);

    BattleUnitState unit = ((BattleQuerySuccess<BattleUnitState>)unitResult).Value;
    return Succeed(session.CanUnitActNow(unit));
  }
}

public sealed class IsUnitStillAvailableThisTurn : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_still_available_this_turn";

  public BattleSession.BattleUnitHandle UnitHandle { get; }

  public IsUnitStillAvailableThisTurn(BattleSession.BattleUnitHandle unitHandle)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    UnitHandle = unitHandle;
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query unit turn availability while the battle is not in progress.");

    BattleQueryResult<BattleUnitState> unitResult = new GetLivingUnit(UnitHandle).Execute(session);
    if (unitResult is BattleQueryFailureResult<BattleUnitState> failureResult)
      return Fail(failureResult.Failure.Reason, failureResult.Failure.Message);

    return Succeed(session.IsUnitStillAvailableThisTurn(UnitHandle));
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

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return session.Board.ValidatePoint(Coordinates).Match(
      point => Succeed(session.Board.CanOccupy(point)),
      () => Succeed(false));
  }
}
