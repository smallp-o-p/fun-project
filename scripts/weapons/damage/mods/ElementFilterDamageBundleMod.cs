using System.Collections.Generic;
using System.Linq;
using FunProject.Stats;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class ElementFilterDamageBundleMod : DamageBundleMod
{
  [Export] public DamageElement Element { get; set; } = DamageElement.Kinetic;
  [Export] public Godot.Collections.Array<StatModifier> Ops { get; set; } = [];

  public override List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context)
    => bundle
      .Select(damage => damage.Element == Element
        ? damage with { Amount = FoldOps(damage.Amount, Ops) }
        : damage)
      .ToList();
}
