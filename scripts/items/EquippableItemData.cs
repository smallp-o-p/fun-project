using FunProject.Core;
using FunProject.Items.Capabilities;
using Godot;
using Godot.Collections;

namespace FunProject.Items;

[GlobalClass]
public partial class EquippableItemData : NamedEntityData
{
  [Export] public Array<ItemCapabilityData> Capabilities { get; set; } = [];
}
