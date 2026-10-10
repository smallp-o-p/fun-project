using System;
using FunProject.Combatants;

namespace FunProject.Battle;

// Owns synchronous automation only. Presentation and committed-event callbacks have no
// execution role; completion observers finish before every fresh policy decision.
internal sealed class EnemyTurnController(BattleRuntime runtime)
{
  private EnemyPolicy? _policy;
  private Faction? _player;
  private bool _stopped;

  internal void Enable(Faction player, EnemyPolicySettings? settings)
  {
    if (_policy is not null) throw new InvalidOperationException("Enemy turns are already enabled.");
    if (_stopped) throw new InvalidOperationException("Cannot enable enemy turns after an execution fault.");
    _player = player;
    _policy = new(settings);
  }

  internal void Stop() => _stopped = true;

  internal bool CanExecuteFor(Faction side)
    => _policy is not null && !_stopped && !ReferenceEquals(side, _player);

  // Runtime supplies the settled enemy side after checking readiness and outcome.
  internal void ExecuteEnemyTurns(Faction side)
  {
    try
    {
      int turn = runtime.Query(new GetTurnNumberQuery());
      var memory = new EnemyTurnMemory();
      while (true)
      {
        var decision = _policy!.Decide(runtime, side, memory);
        var result = runtime.ExecuteSingleAction(decision.Action);
        // Empty interrupted submissions can repeat forever without spending AP.
        // Retry only after a later committed action (for example, reloading) changes state.
        if (result.EventsThatOccurred.IsEmpty) memory.MarkFailed(decision.Intent);
        else memory.ClearFailed();
        if (_stopped || runtime.Query(new GetBattleOutcomeQuery()).IsSome) return;
        var active = runtime.Query(new GetActiveSideQuery());
        // Pass auto-advances an exhausted side; explicit EndTurn does the same.
        if (ReferenceEquals(active, _player)) return;
        int currentTurn = runtime.Query(new GetTurnNumberQuery());
        if (!ReferenceEquals(side, active) || turn != currentTurn) memory = new();
        side = active;
        turn = currentTurn;
      }
    }
    catch { Stop(); throw; }
  }
}
