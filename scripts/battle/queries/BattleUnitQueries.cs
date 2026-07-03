using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class GetUnitAtTile : BattleSessionQuery<Option<BattleUnitState>>
{
  public BattleBoardState.ValidatedPoint Tile { get; }

  public GetUnitAtTile(BattleBoardState.ValidatedPoint tile)
  {
    Tile = tile;
  }

  internal override Either<BattleQueryFailure, Option<BattleUnitState>> Execute(BattleSession session)
  {
    return Succeed(session.GetUnitAt(Tile));
  }
}

public sealed class GetFactionAliveUnits : BattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public Faction Side { get; }

  public GetFactionAliveUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<AliveUnit>> Execute(BattleSession session)
  {
    return Succeed(session.AliveUnits.Where(unit => unit.Side == Side).Select(session.MintAlive).ToArray());
  }
}

public sealed class GetFactionDeadUnits : BattleSessionQuery<IReadOnlyCollection<DeadUnit>>
{
  public Faction Side { get; }

  public GetFactionDeadUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    Side = side;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<DeadUnit>> Execute(BattleSession session)
  {
    return Succeed(session.DeadUnits.Where(unit => unit.Side == Side).Select(session.MintDead).ToArray());
  }
}

public sealed class CanUnitActNow : BattleSessionQuery<bool>
{
  public AliveUnit Unit { get; }
  public CanUnitActNow(AliveUnit unit)
  {
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    return RequireInProgress(session).Bind(_ => Succeed(session.CanUnitActNow(Unit.State)));
  }
}

public sealed class IsUnitStillAvailableThisTurn : BattleSessionQuery<bool>
{
  public AliveUnit Unit { get; }

  public IsUnitStillAvailableThisTurn(AliveUnit unit)
  {
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, bool> Execute(BattleSession session)
  {
    return RequireInProgress(session).Bind(_ => Succeed(session.IsUnitStillAvailableThisTurn(Unit.State)));
  }
}
