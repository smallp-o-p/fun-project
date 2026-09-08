using FunProject.Core;
using FunProject.Items.Effects;

namespace FunProject.Weapons;

public readonly record struct Damage(
  int Amount,
  Element Element,
  Option<StatusEffectSpecData> Status = default,
  DamageKind Kind = DamageKind.Health);

public readonly record struct DamageEmissionContext(int BaseDamage);
