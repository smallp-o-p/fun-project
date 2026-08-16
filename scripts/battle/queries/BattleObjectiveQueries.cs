using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Battle;

// All of a faction's objectives, flipped ones included (history), in add order.
// None when the faction was never given any.
public sealed class GetObjectivesForFaction(Faction side) : IBattleSessionQuery<Option<IReadOnlyList<Objective>>>
{
  public Option<IReadOnlyList<Objective>> Execute(BattleSession session)
  {
    return session.GetObjectives(side) is { Count: > 0 } list ? Some(list) : None;
  }
}
