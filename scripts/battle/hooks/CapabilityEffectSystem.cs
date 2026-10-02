using FunProject.Items.Capabilities;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Hook: when an item is thrown, resolves its blast/effect payload. If the thrown
/// item carries a <see cref="BlastCapability"/>, every effect is applied to the units within
/// the blast radius via <see cref="CapabilityEffectResolver"/>, then a
/// <see cref="CapabilityResolvedBattleEvent"/> is raised. A throwable with no blast simply
/// lands with no effect.
/// </summary>
public sealed class CapabilityEffectSystem : BattleHook<ItemThrownBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, ItemThrownBattleEvent thrown)
  {
    // Blast payloads apply through the running receiver: they are synchronous effects of the
    // throw's step, and a completed event context carries no receiver by contract.
    context.Read.RunningSession.IfSome(session =>
      thrown.Item.FindCapability<BlastCapability>().IfSome(blast =>
      {
        CapabilityEffectResolver.Resolve(session, thrown.Position, blast.BlastRadius, blast.Effects);
        session.RaiseEvents(new CapabilityResolvedBattleEvent(thrown.Item, thrown.Position));
      }));

    return [];
  }
}
