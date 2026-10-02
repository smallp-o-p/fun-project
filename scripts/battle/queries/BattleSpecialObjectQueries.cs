using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class GetUnitAtTile(BattleBoardState.ValidatedPoint tile) : IBattleSessionQuery<Option<BattleUnitState>>
{
  public Option<BattleUnitState> Execute(BattleReadContext context)
  {
    return context.State.GetUnitAt(tile);
  }
}

public sealed class GetFactionAliveUnits(Faction side) : IBattleSessionQuery<IReadOnlyCollection<AliveUnit>>
{
  public IReadOnlyCollection<AliveUnit> Execute(BattleReadContext context)
  {
    return context.State.GetFactionAliveUnits(side).AsValueEnumerable().Select(context.State.MintAlive).ToArray();
  }
}

public sealed class GetFactionDeadUnits(Faction side) : IBattleSessionQuery<IReadOnlyCollection<DeadUnit>>
{
  public IReadOnlyCollection<DeadUnit> Execute(BattleReadContext context)
  {
    return context.State.DeadUnits.AsValueEnumerable().Where(unit => unit.Side == side).Select(context.State.MintDead).ToArray();
  }
}

// Total across the runtime lifetime: false after completion and for dead or foreign units.
public sealed class CanUnitActNow(BattleUnitState unit) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleReadContext context)
  {
    return context.RunningSession.Match(session => session.CanUnitActNow(unit), () => false);
  }
}

public sealed class IsUnitStillAvailableThisTurn(BattleUnitState unit) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleReadContext context)
  {
    return context.RunningSession.Match(session => session.IsUnitStillAvailableThisTurn(unit), () => false);
  }
}

public sealed class GetPlayerFactionQuery : IBattleSessionQuery<Option<Faction>>
{
  public Option<Faction> Execute(BattleReadContext context) => context.State.PlayerFaction;
}
