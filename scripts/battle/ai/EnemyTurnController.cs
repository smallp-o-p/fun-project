using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Weapons;

namespace FunProject.Battle;

// Owns synchronous automation only. Presentation and committed-event callbacks have no
// execution role; completion observers finish before every fresh decision frame.
internal sealed class EnemyTurnController(BattleRuntime runtime)
{
  private const int FactionSubmissionLimit = 64;
  private readonly List<EnemyDecisionTrace> _latest = [];
  private EnemyPolicy? _policy;
  private Faction? _player;
  private long _totalSubmissionLimit;
  private bool _stopped;

  internal IReadOnlyList<EnemyDecisionTrace> LatestDecisions => System.Array.AsReadOnly(_latest.ToArray());

  internal void Enable(Faction player, EnemyPolicySettings? settings, int factionCount)
  {
    if (_policy is not null) throw new InvalidOperationException("Enemy turns are already enabled.");
    if (_stopped) throw new InvalidOperationException("Cannot enable enemy turns after an execution fault.");
    _player = player;
    _policy = new(settings);
    _totalSubmissionLimit = FactionSubmissionLimit * (long)factionCount;
  }

  internal void Stop() => _stopped = true;

  internal void ExecuteEnemyTurns()
  {
    if (_policy is null || _stopped) return;
    try { ExecuteEnabledTurns(_policy); }
    catch { Stop(); throw; }
  }

  private void ExecuteEnabledTurns(EnemyPolicy policy)
  {
    long submitted = 0;
    while (!_stopped && runtime.Query(new GetBattleOutcomeQuery()).IsNone)
    {
      var side = runtime.Query(new GetActiveSideQuery());
      if (ReferenceEquals(side, _player)) return;
      var memory = new EnemyTurnMemory();
      var frame = runtime.Query(new GetEnemyDecisionFrame(side));
      for (int factionSubmissions = 0; factionSubmissions < FactionSubmissionLimit; factionSubmissions++)
      {
        memory.RecordObservations(frame);
        // Reserve the final submission for turn end, even when previous actions were interrupted.
        var decision = factionSubmissions == FactionSubmissionLimit - 1 || submitted == _totalSubmissionLimit - 1
          ? new EnemyDecisionTrace(new(EnemyIntentKind.EndFactionTurn), 0, [])
          : policy.Decide(frame, memory);
        var action = MintAction(side, decision.Intent);
        if (_latest.Count == FactionSubmissionLimit) _latest.RemoveAt(0);
        _latest.Add(decision);
        runtime.ExecuteSingleAction(action);
        submitted++;
        // Disposal/fault can occur inside completion observers; do not query after shutdown.
        if (_stopped) return;
        if (submitted == _totalSubmissionLimit) { Stop(); return; }
        if (!ReferenceEquals(runtime.Query(new GetActiveSideQuery()), side)) break;
        var after = runtime.Query(new GetEnemyDecisionFrame(side));
        if (after.IsTerminal) return;
        if (frame.TurnNumber != after.TurnNumber) break;
        if (!MadeProgress(frame, after)) memory.MarkFailed(decision.Intent);
        frame = after;
      }
    }
  }

  private BattleAction MintAction(Faction side, EnemyIntent intent)
  {
    if (intent.Kind == EnemyIntentKind.EndFactionTurn) return BattleAction.EndFactionTurn(side);
    // Proofs/live references stay on the submission side, never in policy or retained traces.
    var actor = runtime.Query(new GetFactionAliveUnits(side)).AsValueEnumerable()
      .Single(unit => unit.Id == intent.ActorId);
    switch (intent.Kind)
    {
      case EnemyIntentKind.Attack:
        var target = runtime.Query(new GetVisibleHostileUnitsForFaction(side)).AsValueEnumerable()
          .Single(unit => unit.Id == intent.TargetId);
        return BattleAction.AttackEntity(actor, runtime.TryGetAttackTarget(new BattleEntity.Unit(target.State))
          .Match(value => value, () => throw new InvalidOperationException("A fresh visible target must be alive.")));
      case EnemyIntentKind.Reload:
        return BattleAction.ReloadWeapon(actor, (AmmunitionedWeapon)actor.State.RequireEquippedWeapon());
      case EnemyIntentKind.Move:
        return BattleAction.MoveUnit(actor, [runtime.TryGetTile(intent.Destination).Match(value => value,
          () => throw new InvalidOperationException("A permitted neighboring tile must be in bounds."))]);
      case EnemyIntentKind.Pass:
        return BattleAction.PassUnit(actor);
      default:
        throw new InvalidOperationException("Unknown enemy intent.");
    }
  }

  private static bool MadeProgress(EnemyDecisionFrame before, EnemyDecisionFrame after)
    => !before.OwnUnits.AsValueEnumerable().SequenceEqual(after.OwnUnits)
      || !before.Hostiles.AsValueEnumerable().SequenceEqual(after.Hostiles);
}
