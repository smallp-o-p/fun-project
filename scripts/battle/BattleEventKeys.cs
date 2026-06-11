using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

/*
* Gets all the tag keys associated with a particular BattleEvent.
* E.g. for UnitMovedBattleEvent, which implements BattleEvent, IUnitBattleEvent and
* IPositionedBattleEvent, this returns [UnitMovedBattleEvent, IUnitBattleEvent,
* IPositionedBattleEvent, ...] so registrations against tag interfaces also fire.
*/
internal static class BattleEventKeys
{
  internal static IReadOnlyList<Type> For(BattleEvent battleEvent)
  {
    Type eventType = battleEvent.GetType();
    return
    [
      eventType,
      .. eventType
        .GetInterfaces()
        .Where(interfaceType => typeof(BattleEventTag).IsAssignableFrom(interfaceType)),
    ];
  }
}
