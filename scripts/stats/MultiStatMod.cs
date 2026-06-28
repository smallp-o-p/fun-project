using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace FunProject.Stats;

[GlobalClass]
public partial class MultiStatMod : EquippableMod
{
  [Export] public Array<StatMod> StatMods { get; set; } = [];

  public override IEnumerable<StatMod> StatContributions => StatMods;

  public void AddStatMod(StatMod statMod) => StatMods.Add(statMod);
  public bool RemoveStatMod(StatMod statMod) => StatMods.Remove(statMod);
  public void ClearStatMods() => StatMods.Clear();
}
