using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class GetUnitAtTile(BattleBoardState.ValidatedPoint tile) : IBattleSessionQuery<Option<BattleUnitState>>
{
  public Option<BattleUnitState> Execute(BattleSession session)
  {
    return session.GetUnitAt(tile);
  }
}

public sealed class GetFactionAliveUnits(Faction side) : IBattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public IReadOnlyCollection<AliveUnit> Execute(BattleSession session)
  {
    return session.AliveUnits.AsValueEnumerable().Where(unit => unit.Side == side).Select(session.MintAlive).ToArray();
  }
}

public sealed class GetFactionDeadUnits(Faction side) : IBattleSessionQuery<IReadOnlyCollection<DeadUnit>>
{
  public IReadOnlyCollection<DeadUnit> Execute(BattleSession session)
  {
    return session.DeadUnits.AsValueEnumerable().Where(unit => unit.Side == side).Select(session.MintDead).ToArray();
  }
}

public sealed class CanUnitActNow(BattleUnitState unit) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleSession session)
  {
    return session.CanUnitActNow(unit);
  }
}

public sealed class IsUnitStillAvailableThisTurn(BattleUnitState unit) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleSession session)
  {
    return session.IsUnitStillAvailableThisTurn(unit);
  }
}
