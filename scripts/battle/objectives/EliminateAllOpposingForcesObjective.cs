using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class EliminateAllOpposingForcesObjective : Objective
{
  public EliminateAllOpposingForcesObjective(EliminateAllOpposingForcesObjectiveData data) : base(data)
  {
  }

  public override IReadOnlyCollection<Type> ObservedEventKeys { get; } = [typeof(UnitKilledBattleEvent)];

  public override ObjectiveResult Check(Faction owner, BattleEvent battleEvent, BattleSession session)
  {
    if (session.GlobalFactionTurnOrder.AsValueEnumerable().All(side => side == owner || !session.HasLivingUnits(side)))
      return ObjectiveResult.Passed;
    if (!session.HasLivingUnits(owner))
      return ObjectiveResult.Failed;
    return ObjectiveResult.Ongoing;
  }
}
