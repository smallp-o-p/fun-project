using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Content-gated object-expiry hook — NEVER an executor default. Registered only when a
/// battle type declares it or a programmatic caller registers it explicitly. At each turn
/// end, every live object with a <see cref="TimedEffectCapability"/> whose deadline has
/// elapsed expires: status flip + occupancy clear, payload effects resolved through the
/// ordinary CapabilityEffectResolver pipeline (friendly fire, armor, kills, objectives),
/// then the expiry event. Effects resolve BEFORE the event so observers see post-resolution
/// state, mirroring how damage events precede the kill events they cause. This system is
/// object-kind-agnostic: anything from a bomb to a gas valve is authored as the same
/// capability with different effects.
/// </summary>
public sealed class SpecialObjectTimerSystem : BattleHook<TurnEndedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent evt)
  {
    // Expiry resolves against the running receiver: it is synchronous upkeep of the ending
    // turn's step, and completed event contexts carry no receiver by contract.
    if (context.Read.RunningSession.Case is not BattleSession session)
      return [];

    foreach (BattleObjectState obj in session.State.Objects.AsValueEnumerable()
             .Where(obj => obj.Status.IsNone))
    {
      // One lookup: capabilities are fixed at construction, so a live object that carries
      // the trigger here still carries it below — no second lookup or guard needed.
      if (obj.FindCapability<TimedEffectCapability>().Case is not TimedEffectCapability trigger)
        continue;
      if (evt.TurnNumber < trigger.ExpireAfterTurns)
        continue;

      var (expiredObj, position) = session.MarkObjectExpired(obj);
      CapabilityEffectResolver.Resolve(session, position, trigger.EffectRadius, trigger.Effects);
      session.RaiseEvents(new ObjectExpiredBattleEvent(expiredObj, position));
    }

    return [];
  }
}
