using FunProject.Items;
using Godot;

namespace FunProject.GameState;

/// <summary>
/// One armory shelf stamp: the item mold plus its stock policy. Count -1 means unlimited
/// (instantiated fresh on withdraw); otherwise the entry holds exactly Count runtime
/// instances at campaign start.
/// </summary>
[GlobalClass]
public partial class ArmoryEntryData : Resource
{
  [Export] public required EquippableItemData Item { get; set; }
  [Export] public int Count { get; set; } = -1;
}
