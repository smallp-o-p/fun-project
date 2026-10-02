using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class GetGlobalFactionTurnOrderQuery : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<Faction>>
{
  public System.Collections.Generic.IReadOnlyList<Faction> Execute(BattleReadContext context)
    => [.. context.State.Factions];
}

/// <summary>Returns every board object in placement order, regardless of status.</summary>
public sealed class GetBattleSpecialObjectsQuery : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<BattleObjectState>>
{
  public System.Collections.Generic.IReadOnlyList<BattleObjectState> Execute(BattleReadContext context)
    => [.. context.State.Objects];
}

/// <summary>Returns every object whose tile is in the faction's explored-tile memory.</summary>
public sealed class GetFactionVisibleObjectsQuery(Faction faction)
  : IBattleSessionQuery<System.Collections.Generic.IReadOnlyList<BattleObjectState>>
{
  public System.Collections.Generic.IReadOnlyList<BattleObjectState> Execute(BattleReadContext context)
  {
    var exploredTiles = context.State.GetFactionExploredTiles(faction);
    return context.State.Objects
      .AsValueEnumerable()
      .Where(obj => context.State.Board.ValidatePoint(obj.Position).Match(
        Some: point => exploredTiles.Contains(point),
        None: () => false))
      .ToArray();
  }
}
