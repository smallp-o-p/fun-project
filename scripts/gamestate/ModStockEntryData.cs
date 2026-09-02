using FunProject.Stats;
using Godot;

namespace FunProject.GameState;

/// <summary>
/// One mod shelf stamp: the mod template plus its stock policy. Count -1 means unlimited;
/// mods are immutable authored resources, so the shelf is counts only.
/// </summary>
[GlobalClass]
public partial class ModStockEntryData : Resource
{
  [Export] public required EquippableMod Mod { get; set; }
  [Export] public int Count { get; set; } = -1;
}
