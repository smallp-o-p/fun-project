using System;
using System.Collections.Generic;
using FunProject.Core;
using FunProject.Stats;
using Godot;

namespace FunProject.Weapons;

[Tool]
[GlobalClass]
public partial class PacketModifier : Resource
{
  private bool _affectAllElements;

  [Export]
  public bool AffectAllElements
  {
    get => _affectAllElements;
    set
    {
      _affectAllElements = value;
      NotifyPropertyListChanged();
    }
  }

  [Export] public Element Element { get; set; } = Element.Kinetic;
  [Export] public Godot.Collections.Array<StatModifier> Ops { get; set; } = [];
  [Export] public bool Remove { get; set; } = false;

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsStringName() == PropertyName.Element && AffectAllElements)
      property["usage"] = (int)(property["usage"].As<PropertyUsageFlags>() | PropertyUsageFlags.ReadOnly);
  }

  /// <summary>Applies this modifier to the bundle: matched packets (all elements, or the chosen
  /// Element) are removed if Remove, else scaled by Ops; unmatched packets pass through unchanged.</summary>
  public IEnumerable<Damage> Apply(IEnumerable<Damage> bundle)
  {
    foreach (Damage damage in bundle)
    {
      if (!(AffectAllElements || damage.Element == Element))
      {
        yield return damage;
        continue;
      }
      if (Remove)
        continue;
      yield return damage with { Amount = FoldOps(damage.Amount, Ops) };
    }
  }

  private static int FoldOps(int amount, IEnumerable<StatModifier> ops)
  {
    float value = amount;
    foreach (StatModifier op in ops)
    {
      ArgumentNullException.ThrowIfNull(op);
      value = op.Operation switch
      {
        ModifierOperation.Add => value + op.Value,
        ModifierOperation.Multiply => value * op.Value,
        ModifierOperation.CapMin => Mathf.Max(value, op.Value),
        ModifierOperation.CapMax => Mathf.Min(value, op.Value),
        _ => value,
      };
    }

    return Mathf.RoundToInt(value);
  }
}
