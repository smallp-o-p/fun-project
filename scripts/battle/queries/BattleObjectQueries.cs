using FunProject.Combatants;

namespace FunProject.Battle;

public sealed class GetGlobalFactionTurnOrderQuery : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<Faction>>
{
  public System.Collections.Generic.IReadOnlyList<Faction> Execute(BattleSession session)
    => [.. session.GlobalFactionTurnOrder];
}

/// <summary>Omniscient tooling view of every placed object, regardless of status; not faction knowledge.</summary>
public sealed class GetBattleSpecialObjectsQuery : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<BattleObjectState>>
{
  public System.Collections.Generic.IReadOnlyList<BattleObjectState> Execute(BattleSession session)
    => [.. session.Objects];
}

/// <summary>Returns objects whose tile is currently faction visible, without revealing unseen live status.</summary>
public sealed class GetFactionVisibleObjectsQuery(Faction faction)
  : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<BattleObjectState>>
{
  public System.Collections.Generic.IReadOnlyList<BattleObjectState> Execute(BattleSession session)
  {
    var visibleTiles = session.GetFactionVisibleTiles(faction);
    return session.Objects
      .AsValueEnumerable()
      .Where(obj => session.Board.ValidatePoint(obj.Position).Match(
        Some: point => visibleTiles.Contains(point),
        None: () => false))
      .ToArray();
  }
}
