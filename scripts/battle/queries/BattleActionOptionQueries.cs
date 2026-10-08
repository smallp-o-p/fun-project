using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Get every possible action a unit can execute, with an extra field that checks if it can be used.
/// </summary>
public sealed class GetAvailableActionsForUnit(AliveUnit unit) : IBattleSessionQuery<IReadOnlyList<UnitAction>>
{
  public IReadOnlyList<UnitAction> Execute(BattleSession session) => session.ActionOptions.GetFor(unit);
}

public readonly record struct UnitActionFact(UnitActionDefinition Action, bool IsAvailable);

public sealed class GetUnitActionFacts(AliveUnit unit) : IBattleSessionQuery<IReadOnlyList<UnitActionFact>>
{
  public IReadOnlyList<UnitActionFact> Execute(BattleSession session)
  {
    var actions = session.ActionOptions.GetFor(unit);
    var facts = new UnitActionFact[actions.Count];
    for (int i = 0; i < actions.Count; i++) facts[i] = new(actions[i].Action, actions[i].IsAvailable);
    return System.Array.AsReadOnly(facts);
  }
}
