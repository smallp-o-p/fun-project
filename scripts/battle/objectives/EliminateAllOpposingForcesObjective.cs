using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class EliminateAllOpposingForcesObjective : Objective
{
  public EliminateAllOpposingForcesObjective(EliminateAllOpposingForcesObjectiveData data) : base(data)
  {
  }

  public override IReadOnlyCollection<Type> ObservedEventKeys { get; } = [typeof(UnitKilledBattleEvent), typeof(UnitUnconsciousBattleEvent)];

  public override ObjectiveResult Check(Faction owner, BattleEvent battleEvent, BattleReadContext context)
  {
    if (context.State.Factions.AsValueEnumerable().All(side => side == owner || !context.State.HasConsciousUnits(side)))
      return ObjectiveResult.Passed;
    if (!context.State.HasConsciousUnits(owner))
      return ObjectiveResult.Failed;
    return ObjectiveResult.Ongoing;
  }
}
