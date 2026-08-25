using Godot;

namespace FunProject.Battle;

/// <summary>Authored placements for one special board object across one or more cells.</summary>
[GlobalClass]
public partial class ObjectPlacementData : Resource
{
  [Export] public required BattleSpecialObjectData SpecialObject { get; set; }

  [Export] public Godot.Collections.Array<Godot.Vector3I> Positions { get; set; } = [];
}
