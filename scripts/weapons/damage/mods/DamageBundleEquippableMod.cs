using System;
using FunProject.Stats;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class DamageBundleEquippableMod : EquippableMod
{
  [Export] public Godot.Collections.Array<DamageBundleMod> BundleMods { get; set; } = [];

  // Damage mods reshape the emitted bundle; they resolve no stat values.
  public override System.Collections.Generic.Dictionary<Type, float> Apply(HasStats statStick) => [];
}
