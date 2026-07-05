using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed record UnitAction(
  UnitActionDefinition Action,
  bool IsAvailable);

/// <summary>
/// Get every possible action a unit can execute, with an extra field that checks if it can be used.
/// </summary>
public sealed class GetAvailableActionsForUnit(AliveUnit unit) : IBattleSessionQuery<IReadOnlyList<UnitAction>>
{
  public IReadOnlyList<UnitAction> Execute(BattleSession session)
  {
    return UnitActionCatalog.All
      .Where(definition => definition.ExistsFor(unit.State))
      .Select(definition => new UnitAction(
        definition,
        definition.Conditions.All(condition => condition.IsMet(session, unit))))
      .ToList();
  }
}
