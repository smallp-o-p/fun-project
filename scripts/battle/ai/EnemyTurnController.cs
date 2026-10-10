using System;
using System.Linq;
using FunProject.Combatants;

namespace FunProject.Battle;

// Owns synchronous automation only. Presentation and committed-event callbacks have no
// execution role; completion observers finish before every fresh policy decision.
internal sealed class EnemyTurnController(BattleRuntime runtime, EnemyPolicySettings policy)
{
  private readonly EnemyPolicy _policy = new(policy);
  private bool _stopped;

  internal void Stop() => _stopped = true;
  
  /// <summary>
  /// Decide an action for each unit, run it, then decide again once that unit can no longer act.
  /// </summary>
  /// <param name="side"></param>
  internal void ExecuteEnemyActions(Faction side)
  {
    try
    {
      var sideUnitsToAct = runtime.Query(new GetFactionAliveUnits(side))
        .AsValueEnumerable()
        .Where(unit => runtime.Query(new CanUnitActNow(unit.State)))
        .OrderBy(unit => unit.Id)
        .ToList();

      var memory = new EnemyTurnMemory();

      foreach (var unit in sideUnitsToAct)
        memory.Visit(unit.Id, unit.Position.Raw);

      var currUnit = sideUnitsToAct[0];

      do
      {
        if (!ReferenceEquals(side, runtime.Query(new GetActiveSideQuery())))
          break;

        if (!runtime.Query(new CanUnitActNow(currUnit.State)))
        {
          if (sideUnitsToAct.Count == 0)
            break;

          sideUnitsToAct.RemoveAt(0);
          currUnit = sideUnitsToAct[0];
        }

        var decision = _policy.DecideAction(runtime, side, memory, currUnit);

        var result = runtime.ExecuteSingleAction(decision.Action);

        if (result.EventsThatOccurred.IsEmpty)
          memory.MarkFailed(decision.Intent);
        else
          memory.ClearFailed();
      }
      while (!_stopped && !runtime.Query(new GetBattleOutcomeQuery()).IsSome);
    }
    catch { Stop(); throw; }
  }
}
