using FunProject.Core;

namespace FunProject.Weapons;

public readonly record struct Damage(int Amount, Element Element);

public readonly record struct DamageEmissionContext(int BaseDamage);
