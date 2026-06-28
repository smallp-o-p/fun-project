using FunProject.Stats;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class DamageBundleEquippableMod : EquippableMod
{
  [Export] public Godot.Collections.Array<DamageBundleMod> BundleMods { get; set; } = [];
}
