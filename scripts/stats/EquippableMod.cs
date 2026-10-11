using System.Collections.Generic;
using Godot;

namespace FunProject.Stats;

public abstract partial class EquippableMod : Resource
{
  [Export] public string Name { get; set; } = "";
  [Export] public string Description { get; set; } = "";

  // Mod-tier stock policy mirroring EquippableItemData.UnlimitedStock; quantities are
  // runtime Armory state, never authored.
  [Export] public bool UnlimitedStock { get; set; }

  public virtual IEnumerable<StatMod> StatContributions => [];
}
