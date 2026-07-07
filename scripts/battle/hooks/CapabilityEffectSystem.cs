using FunProject.Items.Capabilities;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// After hook: when an item is thrown, resolves its blast/effect payload. If the thrown
/// item carries a <see cref="BlastCapability"/>, every effect is applied to the units within
/// the blast radius via <see cref="CapabilityEffectResolver"/>, then a
/// <see cref="CapabilityResolvedBattleEvent"/> is raised. A throwable with no blast simply
/// lands with no effect.
/// </summary>
public sealed class CapabilityEffectSystem : BattleHook<ItemThrownBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, ItemThrownBattleEvent thrown)
  {
    thrown.Item.FindCapability<BlastCapability>().IfSome(blast =>
    {
      CapabilityEffectResolver.Resolve(context.Session, thrown.Position, blast.BlastRadius, blast.Effects);
      context.Session.RaiseEvents(new CapabilityResolvedBattleEvent(thrown.Item, thrown.Position));
    });

    return [];
  }
}
