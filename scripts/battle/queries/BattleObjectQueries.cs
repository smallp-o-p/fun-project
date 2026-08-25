using FunProject.Combatants;

namespace FunProject.Battle;

public sealed class GetGlobalFactionTurnOrderQuery : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<Faction>>
{
  public System.Collections.Generic.IReadOnlyList<Faction> Execute(BattleSession session)
    => [.. session.GlobalFactionTurnOrder];
}

/// <summary>Returns every board object in placement order, regardless of status.</summary>
public sealed class GetBattleSpecialObjectsQuery : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<BattleObjectState>>
{
  public System.Collections.Generic.IReadOnlyList<BattleObjectState> Execute(BattleSession session)
    => [.. session.Objects];
}

/// <summary>Returns every object whose tile is in the faction's explored-tile memory.</summary>
public sealed class GetFactionVisibleObjectsQuery(Faction faction)
  : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<BattleObjectState>>
{
  public System.Collections.Generic.IReadOnlyList<BattleObjectState> Execute(BattleSession session)
  {
    var exploredTiles = session.GetFactionExploredTiles(faction);
    return session.Objects
      .AsValueEnumerable()
      .Where(obj => session.Board.ValidatePoint(obj.Position).Match(
        Some: point => exploredTiles.Contains(point),
        None: () => false))
      .ToArray();
  }
}
