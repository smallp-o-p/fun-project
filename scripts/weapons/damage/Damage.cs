namespace FunProject.Weapons;

public readonly record struct Damage(int Amount, DamageElement Element);

public readonly record struct DamageEmissionContext(int BaseDamage);
