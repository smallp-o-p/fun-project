using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Get every possible action a unit can execute, with an extra field that checks if it can be used.
/// </summary>
public sealed class GetAvailableActionsForUnit(AliveUnit unit) : IBattleSessionQuery<IReadOnlyList<UnitAction>>
{
  public IReadOnlyList<UnitAction> Execute(BattleReadContext context) => context.State.ActionOptions.GetFor(unit);
}
