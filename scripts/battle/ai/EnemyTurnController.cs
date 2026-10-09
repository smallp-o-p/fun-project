using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Weapons;

namespace FunProject.Battle;

// Owns synchronous automation only. Presentation and committed-event callbacks have no
// execution role; completion observers finish before every fresh decision frame.
internal sealed class EnemyTurnController(BattleRuntime runtime, Faction player,
  EnemyPolicy policy, int initialFactionCount, Func<BattleAction, BattleActionExecResult> submit)
{
  private const int FactionSubmissionLimit = 64;
  private readonly long _drainSubmissionLimit = FactionSubmissionLimit * (long)initialFactionCount;
  private readonly List<EnemyDecisionTrace> _latest = [];
  private bool _stopped;

  internal IReadOnlyList<EnemyDecisionTrace> LatestDecisions => System.Array.AsReadOnly(_latest.ToArray());
  internal string FallbackReason { get; private set; } = "";

  internal void Stop(string reason)
  {
    _stopped = true;
    FallbackReason = reason;
  }

  internal void Drain()
  {
    Faction? activation = null;
    int activationTurn = -1, submittedThisActivation = 0;
    long submittedThisDrain = 0;
    var memory = new EnemyTurnMemory();
    while (!_stopped && !runtime.IsDisposed && runtime.Query(new GetBattleOutcomeQuery()).IsNone)
    {
      var side = runtime.Query(new GetActiveSideQuery());
      if (ReferenceEquals(side, player)) return;
      var frame = runtime.Query(new GetEnemyDecisionFrame(side));
      if (!ReferenceEquals(side, activation) || activationTurn != frame.TurnNumber)
      {
        activation = side;
        activationTurn = frame.TurnNumber;
        submittedThisActivation = 0;
        memory = new();
      }
      memory.Observe(frame);
      bool factionLimit = submittedThisActivation >= FactionSubmissionLimit - 1;
      bool drainLimit = submittedThisDrain >= _drainSubmissionLimit - 1;
      var decision = factionLimit || drainLimit
        ? new EnemyDecisionTrace(new(EnemyIntentKind.EndFactionTurn), 0, [], factionLimit ? "Faction submission limit" : "Drain submission limit")
        : policy.Decide(frame, memory);
      var action = MintAction(side, decision.Intent);
      submittedThisActivation++;
      submittedThisDrain++;
      Record(decision);
      submit(action);
      // Empty interrupted submissions also reach these guards: events are not progress.
      if (_stopped || runtime.IsDisposed || runtime.Query(new GetBattleOutcomeQuery()).IsSome) return;
      if (submittedThisDrain >= _drainSubmissionLimit)
      {
        Stop("Drain submission limit");
        return;
      }
      if (!ReferenceEquals(runtime.Query(new GetActiveSideQuery()), side)) continue;
      var after = runtime.Query(new GetEnemyDecisionFrame(side));
      memory.Observe(after);
      if (!MadeProgress(frame, after))
      {
        memory.Suppress(decision.Intent);
        _latest[^1] = decision with { FallbackReason = "No progress; intent suppressed" };
        FallbackReason = "No progress; intent suppressed";
      }
    }
  }

  private void Record(EnemyDecisionTrace decision)
  {
    if (_latest.Count == FactionSubmissionLimit) _latest.RemoveAt(0);
    _latest.Add(decision);
    FallbackReason = decision.FallbackReason;
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
    => before.TurnNumber != after.TurnNumber || after.IsTerminal
      || !before.OwnUnits.AsValueEnumerable().SequenceEqual(after.OwnUnits)
      || !before.Hostiles.AsValueEnumerable().SequenceEqual(after.Hostiles);
}
