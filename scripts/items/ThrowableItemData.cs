using Godot;

namespace FunProject.Items;

[GlobalClass]
public partial class ThrowableItemData : EquippableItemData
{
  [Export] public int ThrowRange { get; set; } = 8;
  [Export] public int ActionPointCost { get; set; } = 1;
  [Export] public bool ConsumesOnUse { get; set; } = true;
}
