using Godot;
namespace FunProject.Weapons;

[GlobalClass]
public partial class AmmunitionedWeaponData : WeaponData
{
  [Export] required public Stats.AmmunitionStat AmmunitionStat { get; set; }
  [Export] required public AmmunitionData DefaultAmmoData { get; set; }
}
