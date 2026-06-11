using System.Collections.Generic;
using System.Linq;
using FunProject.Core;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class RemoveElementDamageBundleMod : DamageBundleMod
{
  [Export] public Element Element { get; set; } = Element.Kinetic;

  public override List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context)
    => bundle.Where(damage => damage.Element != Element).ToList();
}
