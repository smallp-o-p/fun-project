using FunProject.Battle;
using Godot;

namespace FunProject.Buffs;

/// <summary>True while a living enemy occupies an adjacent tile, orthogonally or diagonally.</summary>
[GlobalClass]
public partial class AdjacentEnemyCondition : BuffCondition
{
  internal override bool IsMet(BattleReadContext context, BattleUnitState unit)
    => context.State.GetUnitPosition(unit).Match(
         position => context.State.AliveUnits
           .AsValueEnumerable().Where(other => other.Side != unit.Side)
           .Any(other => context.State.GetUnitPosition(other).Match(
             otherPosition => BattleBoardState.AreAdjacent(position, otherPosition),
             () => false)),
         () => false);
}
