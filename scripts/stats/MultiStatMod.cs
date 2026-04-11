using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace FunProject.Stats;

[GlobalClass]
public partial class MultiStatMod : EquippableMod
{
  [Export] public Array<StatMod> StatMods { get; set; } = [];

  public void AddStatMod(StatMod statMod) => StatMods.Add(statMod);

  public bool RemoveStatMod(StatMod statMod) => StatMods.Remove(statMod);

  public void ClearStatMods() => StatMods.Clear();

  public override System.Collections.Generic.Dictionary<Type, float> Apply(HasStats statStick)
  {
    var results = new System.Collections.Generic.Dictionary<Type, float>();

    foreach (var statMod in StatMods)
    {
      if (!results.TryAdd(statMod.TargetType, statMod.Apply(statStick)))
        throw new InvalidOperationException();
    }

    return results;
  }
}
