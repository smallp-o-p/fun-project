using FunProject.Core;
using Godot;

namespace FunProject.Items;

[GlobalClass]
public partial class EquippableItemData : NamedEntityData
{
  [Export] public int ModSlotCount { get; set; }
  [Export] public int MaxCharges { get; set; } = 1;
}
