using FunProject.Items.Effects;
using FunProject.Weapons;

namespace FunProject.Battle;

/// <summary>
/// Runtime status effect that deals recurring damage at the end of the owning faction's
/// turn, driven by an authored <see cref="DamageOverTimeStatusSpecData"/>. Damage goes
/// through the normal <see cref="BattleSession.ApplyDamageTo"/> pipeline.
/// </summary>
public sealed class DamageOverTimeStatusEffect : ActiveStatusEffect
{
  private readonly DamageOverTimeStatusSpecData _spec;

  internal DamageOverTimeStatusEffect(DamageOverTimeStatusSpecData spec)
    : base(spec)
  {
    _spec = spec;
  }

  internal override void OnFactionTurnEnd(BattleSession session, BattleUnitState unit)
  {
    if (_spec.TickDamage > 0)
      session.ApplyDamageTo(unit, [new Damage(_spec.TickDamage, _spec.TickElement)]);
  }
}
