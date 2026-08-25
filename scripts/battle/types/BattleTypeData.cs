using FunProject.Core;
using Godot;

namespace FunProject.Battle;

/// <summary>Authored battle-type resource: map pool, faction deployments, objects, and systems.</summary>
[GlobalClass]
public partial class BattleTypeData : NamedEntityData
{
  [Export] public Godot.Collections.Array<BattleMapData> MapPool { get; set; } = [];

  [Export] public Godot.Collections.Array<FactionDeploymentData> Factions { get; set; } = [];

  [Export] public int PlayerFactionIndex { get; set; } = 0;

  [Export] public Godot.Collections.Array<ObjectPlacementData> Objects { get; set; } = [];

  [Export] public Godot.Collections.Array<BattleTypeSystemData> Systems { get; set; } = [];
}
