using Godot;
namespace FunProject.Weapons;

[GlobalClass]
public partial class AmmunitionedWeaponData : WeaponData
{
  [Export] public Stats.AmmunitionStat AmmunitionStat { get; set; }
  [Export] public AmmunitionData DefaultAmmoData { get; set; }
}
