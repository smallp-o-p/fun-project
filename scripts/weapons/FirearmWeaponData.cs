using Godot;

namespace FunProject.Weapons;

public enum FirearmArchetype
{
  Pistol,
  SniperRifle,
  AssaultRifle,
  Shotgun,
}

[GlobalClass]
public partial class FirearmWeaponData : AmmunitionedWeaponData
{
  [Export] public FirearmArchetype Archetype { get; set; }
}
