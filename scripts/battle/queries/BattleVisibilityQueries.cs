using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class IsUnitVisibleToUnit : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_visible_to_unit";

  public BattleUnitState ObserverUnit { get; }
  public BattleUnitState TargetUnit { get; }
  internal int ObserverUnitId => ObserverUnit.Id;
  internal int TargetUnitId => TargetUnit.Id;

  public IsUnitVisibleToUnit(BattleUnitState observerUnit, BattleUnitState targetUnit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(observerUnit);
    ArgumentNullException.ThrowIfNull(targetUnit);
    ObserverUnit = observerUnit;
    TargetUnit = targetUnit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    Option<BattleQueryFailure> observerFailure = ValidateLivingUnit(session, ObserverUnit, "Observer");
    return observerFailure.Match(
      Fail,
      () =>
      {
        Option<BattleQueryFailure> targetFailure = ValidateLivingUnit(session, TargetUnit, "Target");
        return targetFailure.Match(
          Fail,
          () =>
          {
            if (ObserverUnitId == TargetUnitId)
              return Succeed(true);

            return Succeed(ObserverUnit.VisibleUnits.Contains(TargetUnit));
          });
      });
  }

  private static Option<BattleQueryFailure> ValidateLivingUnit(BattleSession session, BattleUnitState unit, string role)
  {
    if (!unit.BelongsTo(session))
      return Some(new BattleQueryFailure(BattleQueryFailureReason.UnknownUnit, $"{role} unit {unit.Id} could not be resolved. Unknown unit id {unit.Id}."));
    if (!unit.IsAlive)
      return Some(new BattleQueryFailure(BattleQueryFailureReason.UnitNotAlive, $"{role} unit {unit.Id} could not be resolved. Unit {unit.Id} is not alive."));

    return None;
  }
}

public sealed class IsUnitVisibleToFaction : BattleSessionQuery<bool>
{
  public const string Id = "is_unit_visible_to_faction";

  public Faction Faction { get; }
  public BattleUnitState TargetUnit { get; }
  internal int TargetUnitId => TargetUnit.Id;

  public IsUnitVisibleToFaction(Faction faction, BattleUnitState targetUnit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(targetUnit);
    Faction = faction;
    TargetUnit = targetUnit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!TargetUnit.BelongsTo(session))
      return Fail(BattleQueryFailureReason.UnknownUnit, $"Target unit {TargetUnitId} could not be resolved. Unknown unit id {TargetUnitId}.");
    if (!TargetUnit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Target unit {TargetUnitId} could not be resolved. Unit {TargetUnitId} is not alive.");
    if (TargetUnit.Side == Faction)
      return Succeed(true);

    return Succeed(session.IsUnitVisibleToFaction(Faction, TargetUnit));
  }
}

public sealed class IsTileVisibleToFaction : BattleSessionQuery<bool>
{
  public const string Id = "is_tile_visible_to_faction";

  public Faction Faction { get; }
  public BattleBoardState.ValidatedPoint Tile { get; }

  public IsTileVisibleToFaction(Faction faction, BattleBoardState.ValidatedPoint tile)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
    Tile = tile;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.GetFactionVisibleTiles(Faction).Contains(Tile));
  }
}

public sealed class HasFactionExploredTile : BattleSessionQuery<bool>
{
  public const string Id = "has_faction_explored_tile";

  public Faction Faction { get; }
  public BattleBoardState.ValidatedPoint Tile { get; }

  public HasFactionExploredTile(Faction faction, BattleBoardState.ValidatedPoint tile)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
    Tile = tile;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.GetFactionExploredTiles(Faction).Contains(Tile));
  }
}

public sealed class GetVisibleUnitsForUnit : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_visible_units_for_unit";

  public BattleUnitState ObserverUnit { get; }
  internal int ObserverUnitId => ObserverUnit.Id;

  public GetVisibleUnitsForUnit(BattleUnitState observerUnit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(observerUnit);
    ObserverUnit = observerUnit;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!ObserverUnit.BelongsTo(session))
      return Fail(BattleQueryFailureReason.UnknownUnit, $"Observer unit {ObserverUnitId} could not be resolved. Unknown unit id {ObserverUnitId}.");
    if (!ObserverUnit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Observer unit {ObserverUnitId} could not be resolved. Unit {ObserverUnitId} is not alive.");

    return Succeed(ObserverUnit.VisibleUnits.ToArray());
  }
}

public sealed class GetVisibleEnemiesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleUnitState>>
{
  public const string Id = "get_visible_enemies_for_unit";

  public BattleUnitState ObserverUnit { get; }
  internal int ObserverUnitId => ObserverUnit.Id;

  public GetVisibleEnemiesForUnit(BattleUnitState observerUnit)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(observerUnit);
    ObserverUnit = observerUnit;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!ObserverUnit.BelongsTo(session))
      return Fail(BattleQueryFailureReason.UnknownUnit, $"Observer unit {ObserverUnitId} could not be resolved. Unknown unit id {ObserverUnitId}.");
    if (!ObserverUnit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Observer unit {ObserverUnitId} could not be resolved. Unit {ObserverUnitId} is not alive.");

    return Succeed(ObserverUnit.VisibleUnits
      .Where(unit => unit.Side != ObserverUnit.Side)
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
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleUnitState>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return Succeed(session.AliveUnits
      .Where(unit => session.IsUnitVisibleToFaction(Faction, unit))
      .ToArray());
  }
}

public sealed class GetVisibleTilesForFaction : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public const string Id = "get_visible_tiles_for_faction";

  public Faction Faction { get; }

  public GetVisibleTilesForFaction(Faction faction)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.GetFactionVisibleTiles(Faction).ToArray());
  }
}

public sealed class GetExploredTilesForFaction : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public const string Id = "get_explored_tiles_for_faction";

  public Faction Faction { get; }

  public GetExploredTilesForFaction(Faction faction)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return Succeed(session.GetFactionExploredTiles(Faction).ToArray());
  }
}
