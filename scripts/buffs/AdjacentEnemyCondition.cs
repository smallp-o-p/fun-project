using FunProject.Battle;
using Godot;

namespace FunProject.Buffs;

/// <summary>True while a living enemy occupies an orthogonally adjacent tile.</summary>
[GlobalClass]
public partial class AdjacentEnemyCondition : BuffCondition
{
  internal override bool IsMet(BattleSession session, BattleUnitState unit)
    => session.GetUnitPosition(unit).Match(
         position => session.AliveUnits
           .AsValueEnumerable().Where(other => other.Side != unit.Side)
           .Any(other => session.GetUnitPosition(other).Match(
             otherPosition => BattleBoardState.AreAdjacent(position, otherPosition),
             () => false)),
         () => false);
}
