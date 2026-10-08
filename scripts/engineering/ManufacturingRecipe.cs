using FunProject.Currencies;
using Godot;
using FunProject.Items;

namespace FunProject.Engineering;

[GlobalClass]
public partial class ManufacturingRecipe : Resource
{
  [Export] public required EquippableItemData Item { get; set; }

  [Export(PropertyHint.Range, "1,4294967295,1")] public uint DurationDays { get; set; } = 1;

  [Export] public Godot.Collections.Dictionary<Currency, long> Costs { get; set; } = [];
}
