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
  private bool _activationPending;
  private bool _executing;

  internal void Enable(Faction player, EnemyPolicySettings? settings)
  {
    if (_policy is not null) throw new InvalidOperationException("Enemy turns are already enabled.");
    if (_stopped) throw new InvalidOperationException("Cannot enable enemy turns after an execution fault.");
    _player = player;
    _policy = new(settings);
    // First-turn bootstrap has no ActiveSideChanged event.
    OnFactionActivated(runtime.Query(new GetActiveSideQuery()));
  }

  internal void Stop() => _stopped = true;

  internal void OnFactionActivated(Faction side)
  {
    if (_policy is not null && !_stopped && !_executing)
      _activationPending = !ReferenceEquals(side, _player);
  }

  internal void ExecutePendingEnemyTurns()
  {
    if (!_activationPending || _policy is null || _stopped || _executing) return;
    _activationPending = false;
    _executing = true;
    try
    {
      Faction? side = null;
      int turn = 0;
      var memory = new EnemyTurnMemory();
      while (!_stopped && runtime.Query(new GetBattleOutcomeQuery()).IsNone)
      {
        var active = runtime.Query(new GetActiveSideQuery());
        // Pass auto-advances an exhausted side; explicit EndTurn does the same.
        // Either can return player control after any submission in this loop.
        if (ReferenceEquals(active, _player)) return;
        int currentTurn = runtime.Query(new GetTurnNumberQuery());
        if (side is not null && (!ReferenceEquals(side, active) || turn != currentTurn)) memory = new();
        side = active;
        turn = currentTurn;
        var decision = _policy.Decide(runtime, side, memory);
        var result = runtime.ExecuteSingleAction(decision.Action);
        // Empty interrupted submissions can repeat forever without spending AP.
        // Retry only after a later committed action (for example, reloading) changes state.
        if (result.EventsThatOccurred.IsEmpty) memory.MarkFailed(decision.Intent);
        else memory.ClearFailed();
      }
    }
    catch { Stop(); throw; }
    finally { _executing = false; }
  }
}
