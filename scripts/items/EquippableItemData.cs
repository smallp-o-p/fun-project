using FunProject.Core;
using FunProject.Items.Capabilities;
using Godot;
using Godot.Collections;

namespace FunProject.Items;

[GlobalClass]
public partial class EquippableItemData : NamedEntityData
{
  [Export] public Array<ItemCapabilityData> Capabilities { get; set; } = [];

  // Item-tier stock policy, shared by every campaign using this mold (XCOM's template bit):
  // unlimited items instantiate on withdraw; scarce items hold counted runtime instances.
  // Quantities themselves are never authored — they live in the runtime Armory.
  [Export] public bool UnlimitedStock { get; set; }
}
