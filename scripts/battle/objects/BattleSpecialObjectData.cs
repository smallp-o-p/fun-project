using FunProject.Core;
using Godot;

namespace FunProject.Battle;

/// <summary>Authored template for a special board object and its runtime capabilities.</summary>
[GlobalClass]
public partial class BattleSpecialObjectData : NamedEntityData
{
  [Export] public Godot.Collections.Array<SpecialObjectCapabilityData> Capabilities { get; set; } = [];
}
