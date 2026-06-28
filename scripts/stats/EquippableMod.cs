using System.Collections.Generic;
using Godot;

namespace FunProject.Stats;

public abstract partial class EquippableMod : Resource
{
  [Export] public string Name { get; set; } = "";
  [Export] public string Description { get; set; } = "";

  public virtual IEnumerable<StatMod> StatContributions => [];
}
