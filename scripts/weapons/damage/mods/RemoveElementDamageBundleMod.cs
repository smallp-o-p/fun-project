using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class RemoveElementDamageBundleMod : DamageBundleMod
{
  [Export] public DamageElement Element { get; set; } = DamageElement.Kinetic;

  public override List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context)
    => bundle.Where(damage => damage.Element != Element).ToList();
}
