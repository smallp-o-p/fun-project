using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class GetUnit : BattleSessionQuery<BattleUnitState>
{
  public const string Id = "get_unit";

  public int UnitId { get; }

  public GetUnit(int unitId)
    : base(Id)
  {
    UnitId = unitId;
  }

  internal override BattleQueryResult<BattleUnitState> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    var unit = session.GetUnitOrNull(UnitId);
    if (unit == null)
      return Fail(BattleQueryFailureReason.UnknownUnit, $"Unknown unit id {UnitId}.");

    return Succeed(unit);
  }
}

public sealed class GetLivingUnit : BattleSessionQuery<BattleUnitState>
{
  public const string Id = "get_living_unit";

  public int UnitId { get; }

  public GetLivingUnit(int unitId)
    : base(Id)
  {
    UnitId = unitId;
  }

  internal override BattleQueryResult<BattleUnitState> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    var unit = session.GetLivingUnitOrNull(UnitId);
    if (unit != null)
      return Succeed(unit);

    var trackedUnit = session.GetUnitOrNull(UnitId);
    if (trackedUnit == null)
      return Fail(BattleQueryFailureReason.UnknownUnit, $"Unknown unit id {UnitId}.");

    return Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {UnitId} is not alive.");
  }
}

public sealed class GetFactionAliveUnits : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_faction_alive_units";

  public Faction Side { get; }

  public GetFactionAliveUnits(Faction side)
    : base(Id)
  {
    Side = side ?? throw new ArgumentNullException(nameof(side));
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
    Side = side ?? throw new ArgumentNullException(nameof(side));
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

  public int UnitId { get; }

  public CanUnitActNow(int unitId)
    : base(Id)
  {
    UnitId = unitId;
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query active unit state while the battle is not in progress.");

    BattleQueryResult<BattleUnitState> unitResult = new GetLivingUnit(UnitId).Execute(session);
    if (unitResult is BattleQueryFailureResult<BattleUnitState> failureResult)
      return Fail(failureResult.Failure.Reason, failureResult.Failure.Message);

    BattleUnitState unit = ((BattleQuerySuccess<BattleUnitState>)unitResult).Value;
    return Succeed(session.CanUnitActNow(unit));
  }
}

public sealed class IsUnitStillAvailableThisTurn : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_still_available_this_turn";

  public int UnitId { get; }

  public IsUnitStillAvailableThisTurn(int unitId)
    : base(Id)
  {
    UnitId = unitId;
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (session.Phase != BattlePhase.InProgress)
      return Fail(BattleQueryFailureReason.InvalidBattleState, "Cannot query unit turn availability while the battle is not in progress.");

    BattleQueryResult<BattleUnitState> unitResult = new GetLivingUnit(UnitId).Execute(session);
    if (unitResult is BattleQueryFailureResult<BattleUnitState> failureResult)
      return Fail(failureResult.Failure.Reason, failureResult.Failure.Message);

    return Succeed(session.IsUnitStillAvailableThisTurn(UnitId));
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
    return Succeed(session.Board.CanOccupy(Coordinates));
  }
}
