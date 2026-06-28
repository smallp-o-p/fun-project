using FunProject.Items.Capabilities;

namespace FunProject.Battle;

/// <summary>
/// Bookkeeping system: when an item is thrown, resolves its blast/effect payload. If the
/// thrown item carries a <see cref="BlastCapability"/>, every effect is applied to the units
/// within the blast radius via <see cref="CapabilityEffectResolver"/>, then a
/// <see cref="CapabilityResolvedBattleEvent"/> is raised. A throwable with no blast simply
/// lands with no effect. This listener mutates state and raises follow-up events only; it
/// never submits executor actions.
/// </summary>
public sealed class CapabilityEffectSystem : BattleEventListener<ItemThrownBattleEvent>
{
  protected override void OnEvent(BattleSession session, ItemThrownBattleEvent thrown)
  {
    thrown.Item.FindCapability<BlastCapability>().IfSome(blast =>
    {
      CapabilityEffectResolver.Resolve(session, thrown.Position, blast.BlastRadius, blast.Effects);
      session.RaiseEvent(new CapabilityResolvedBattleEvent(thrown.Item, thrown.Position));
    });
  }
}
