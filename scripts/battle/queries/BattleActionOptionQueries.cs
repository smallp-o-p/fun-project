using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

// One row of the answer: a verb the unit HAS, plus the first availability condition it fails
// (None -> available right now). The failed condition's concrete type is the machine-readable
// reason; presentation decides per verb/reason whether to grey out or hide.
public sealed record AvailableUnitAction(
  UnitActionDefinition Action,
  Option<UnitActionCondition> FailedCondition)
{
  public bool IsAvailable => FailedCondition.IsNone;
}

// Reports every verb that exists for the unit, in catalog (display) order, with availability.
// Cheap, no board BFS: expensive candidate sets (reachable tiles, attackable enemies) stay on
// the presentation-side targeting handlers. Callers cache the result on selection.
public sealed class GetAvailableActionsForUnit : BattleSessionQuery<IReadOnlyList<AvailableUnitAction>>
{
  public BattleUnitState Unit { get; }

  public GetAvailableActionsForUnit(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
  }

  internal override Either<BattleQueryFailure, IReadOnlyList<AvailableUnitAction>> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    IReadOnlyList<AvailableUnitAction> actions = UnitActionCatalog.All
      .Where(definition => definition.ExistsFor(Unit))
      .Select(definition => new AvailableUnitAction(
        definition,
        Optional(definition.Conditions.FirstOrDefault(condition => !condition.IsMet(session, Unit)))))
      .ToList();

    return Succeed(actions);
  }
}
