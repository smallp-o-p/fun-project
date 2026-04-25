using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class IsUnitVisibleToUnit : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_visible_to_unit";

  public BattleSession.BattleUnitHandle ObserverUnitHandle { get; }
  public BattleSession.BattleUnitHandle TargetUnitHandle { get; }
  internal int ObserverUnitId => ObserverUnitHandle.UnitId;
  internal int TargetUnitId => TargetUnitHandle.UnitId;

  public IsUnitVisibleToUnit(BattleSession.BattleUnitHandle observerUnitHandle, BattleSession.BattleUnitHandle targetUnitHandle)
    : base(Id)
  {
    ObserverUnitHandle = observerUnitHandle ?? throw new ArgumentNullException(nameof(observerUnitHandle));
    TargetUnitHandle = targetUnitHandle ?? throw new ArgumentNullException(nameof(targetUnitHandle));
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    BattleQueryResult<BattleUnitState> observerResult = new GetLivingUnit(ObserverUnitHandle).Execute(session);
    if (observerResult is BattleQueryFailureResult<BattleUnitState> observerFailure)
      return Fail(observerFailure.Failure.Reason, $"Observer unit {ObserverUnitId} could not be resolved. {observerFailure.Failure.Message}");

    BattleQueryResult<BattleUnitState> targetResult = new GetLivingUnit(TargetUnitHandle).Execute(session);
    if (targetResult is BattleQueryFailureResult<BattleUnitState> targetFailure)
      return Fail(targetFailure.Failure.Reason, $"Target unit {TargetUnitId} could not be resolved. {targetFailure.Failure.Message}");

    if (ObserverUnitId == TargetUnitId)
      return Succeed(true);

    return Succeed(session.VisibilitySnapshot
      .GetVisibleUnitsForObserverOrEmpty(ObserverUnitId)
      .Contains(TargetUnitId));
  }
}

public sealed class IsUnitVisibleToFaction : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_visible_to_faction";

  public Faction Faction { get; }
  public BattleSession.BattleUnitHandle TargetUnitHandle { get; }
  internal int TargetUnitId => TargetUnitHandle.UnitId;

  public IsUnitVisibleToFaction(Faction faction, BattleSession.BattleUnitHandle targetUnitHandle)
    : base(Id)
  {
    Faction = faction ?? throw new ArgumentNullException(nameof(faction));
    TargetUnitHandle = targetUnitHandle ?? throw new ArgumentNullException(nameof(targetUnitHandle));
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    BattleQueryResult<BattleUnitState> targetResult = new GetLivingUnit(TargetUnitHandle).Execute(session);
    if (targetResult is BattleQueryFailureResult<BattleUnitState> targetFailure)
      return Fail(targetFailure.Failure.Reason, $"Target unit {TargetUnitId} could not be resolved. {targetFailure.Failure.Message}");

    BattleUnitState target = ((BattleQuerySuccess<BattleUnitState>)targetResult).Value;
    if (target.Side == Faction)
      return Succeed(true);

    return Succeed(session.VisibilitySnapshot
      .GetFactionStateOrEmpty(Faction)
      .VisibleForeignUnitIds
      .Contains(TargetUnitId));
  }
}

public sealed class IsTileVisibleToFaction : BattleSessionQuery<bool>
{
  public const string Id = "is_tile_visible_to_faction";

  public Faction Faction { get; }
  public Vector3I Tile { get; }

  public IsTileVisibleToFaction(Faction faction, Vector3I tile)
    : base(Id)
  {
    Faction = faction ?? throw new ArgumentNullException(nameof(faction));
    Tile = tile;
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!session.Board.IsInBounds(Tile))
      return Fail(BattleQueryFailureReason.InvalidTile, $"Tile {Tile} is outside the battle board.");

    return Succeed(session.VisibilitySnapshot
      .GetFactionStateOrEmpty(Faction)
      .VisibleTiles
      .Contains(Tile));
  }
}

public sealed class HasFactionExploredTile : BattleSessionQuery<bool>
{
  public const string Id = "has_faction_explored_tile";

  public Faction Faction { get; }
  public Vector3I Tile { get; }

  public HasFactionExploredTile(Faction faction, Vector3I tile)
    : base(Id)
  {
    Faction = faction ?? throw new ArgumentNullException(nameof(faction));
    Tile = tile;
  }

  internal override BattleQueryResult<bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!session.Board.IsInBounds(Tile))
      return Fail(BattleQueryFailureReason.InvalidTile, $"Tile {Tile} is outside the battle board.");

    return Succeed(session.VisibilitySnapshot
      .GetFactionStateOrEmpty(Faction)
      .ExploredTiles
      .Contains(Tile));
  }
}

public sealed class GetVisibleUnitsForUnit : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_visible_units_for_unit";

  public BattleSession.BattleUnitHandle ObserverUnitHandle { get; }
  internal int ObserverUnitId => ObserverUnitHandle.UnitId;

  public GetVisibleUnitsForUnit(BattleSession.BattleUnitHandle observerUnitHandle)
    : base(Id)
  {
    ObserverUnitHandle = observerUnitHandle ?? throw new ArgumentNullException(nameof(observerUnitHandle));
  }

  internal override BattleQueryResult<IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    BattleQueryResult<BattleUnitState> observerResult = new GetLivingUnit(ObserverUnitHandle).Execute(session);
    if (observerResult is BattleQueryFailureResult<BattleUnitState> observerFailure)
      return Fail(observerFailure.Failure.Reason, $"Observer unit {ObserverUnitId} could not be resolved. {observerFailure.Failure.Message}");

    IReadOnlySet<int> visibleUnitIds = session.VisibilitySnapshot.GetVisibleUnitsForObserverOrEmpty(ObserverUnitId);
    return Succeed(session.AliveUnits
      .Where(unit => visibleUnitIds.Contains(unit.UnitId))
      .ToArray());
  }
}

public sealed class GetVisibleEnemiesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_visible_enemies_for_unit";

  public BattleSession.BattleUnitHandle ObserverUnitHandle { get; }
  internal int ObserverUnitId => ObserverUnitHandle.UnitId;

  public GetVisibleEnemiesForUnit(BattleSession.BattleUnitHandle observerUnitHandle)
    : base(Id)
  {
    ObserverUnitHandle = observerUnitHandle ?? throw new ArgumentNullException(nameof(observerUnitHandle));
  }

  internal override BattleQueryResult<IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    BattleQueryResult<BattleUnitState> observerResult = new GetLivingUnit(ObserverUnitHandle).Execute(session);
    if (observerResult is BattleQueryFailureResult<BattleUnitState> observerFailure)
      return Fail(observerFailure.Failure.Reason, $"Observer unit {ObserverUnitId} could not be resolved. {observerFailure.Failure.Message}");

    BattleUnitState observer = ((BattleQuerySuccess<BattleUnitState>)observerResult).Value;
    IReadOnlySet<int> visibleUnitIds = session.VisibilitySnapshot.GetVisibleUnitsForObserverOrEmpty(ObserverUnitId);
    return Succeed(session.AliveUnits
      .Where(unit => unit.Side != observer.Side && visibleUnitIds.Contains(unit.UnitId))
      .ToArray());
  }
}

public sealed class GetVisibleUnitsForFaction : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_visible_units_for_faction";

  public Faction Faction { get; }

  public GetVisibleUnitsForFaction(Faction faction)
    : base(Id)
  {
    Faction = faction ?? throw new ArgumentNullException(nameof(faction));
  }

  internal override BattleQueryResult<IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    IReadOnlySet<int> visibleForeignUnitIds = session.VisibilitySnapshot
      .GetFactionStateOrEmpty(Faction)
      .VisibleForeignUnitIds;

    return Succeed(session.AliveUnits
      .Where(unit => unit.Side == Faction || visibleForeignUnitIds.Contains(unit.UnitId))
      .ToArray());
  }
}

public sealed class GetVisibleTilesForFaction : BattleSessionQuery<IReadOnlyCollection<Vector3I>>
{
  public const string Id = "get_visible_tiles_for_faction";

  public Faction Faction { get; }

  public GetVisibleTilesForFaction(Faction faction)
    : base(Id)
  {
    Faction = faction ?? throw new ArgumentNullException(nameof(faction));
  }

  internal override BattleQueryResult<IReadOnlyCollection<Vector3I>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.VisibilitySnapshot.GetFactionStateOrEmpty(Faction).VisibleTiles.ToArray());
  }
}

public sealed class GetExploredTilesForFaction : BattleSessionQuery<IReadOnlyCollection<Vector3I>>
{
  public const string Id = "get_explored_tiles_for_faction";

  public Faction Faction { get; }

  public GetExploredTilesForFaction(Faction faction)
    : base(Id)
  {
    Faction = faction ?? throw new ArgumentNullException(nameof(faction));
  }

  internal override BattleQueryResult<IReadOnlyCollection<Vector3I>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.VisibilitySnapshot.GetFactionStateOrEmpty(Faction).ExploredTiles.ToArray());
  }
}
