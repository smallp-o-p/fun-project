using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System.Collections.Generic;

namespace FunProject.Scenes;

/// <summary>Presents alive units as capsule meshes and registers them with the playback director.</summary>
public sealed partial class BattleUnitMeshes : Node3D
{
  public const uint PickingLayer = 4;
  private readonly Dictionary<GodotObject, BattleUnitState> _unitsByHitbox = [];

  public Option<BattleUnitState> GetUnitForHitbox(GodotObject collider) =>
    _unitsByHitbox.TryGetValue(collider, out var unit) ? Some(unit) : None;

  public void Initialize(BattleRuntime runtime, EventPlaybackDirector director, Faction playerFaction)
  {
    var capsule = new CapsuleMesh { Radius = 0.3f, Height = 1.0f };

    foreach (Faction faction in runtime.Query(new GetGlobalFactionTurnOrderQuery()))
    {
      IReadOnlyCollection<AliveUnit> units = runtime.Query(new GetFactionAliveUnits(faction));

      var material = new StandardMaterial3D
      {
        AlbedoColor = faction == playerFaction ? Colors.SteelBlue : Colors.IndianRed,
      };

      foreach (AliveUnit unit in units)
      {
        Vector3I tile = unit.Position.Raw;

        var mesh = new MeshInstance3D
        {
          Mesh = capsule,
          MaterialOverride = material,
          Position = BoardCoordinates.TileToWorldCenter(tile) + new Vector3(0f, 0.5f, 0f),
        };
        AddChild(mesh);
        // Match the rendered capsule; the hitbox follows playback transforms without
        // becoming a movement obstacle or participating in Godot's later object picking.
        var hitbox = new Area3D
        {
          Name = "PickingHitbox",
          CollisionLayer = PickingLayer,
          CollisionMask = 0,
          Monitoring = false,
          Monitorable = false,
          InputRayPickable = false,
        };
        hitbox.AddChild(new CollisionShape3D
        {
          Shape = new CapsuleShape3D { Radius = capsule.Radius, Height = capsule.Height },
        });
        mesh.AddChild(hitbox);
        _unitsByHitbox.Add(hitbox, unit.State);
        mesh.VisibilityChanged += () => hitbox.CollisionLayer = mesh.IsVisibleInTree() ? PickingLayer : 0;
        director.RegisterUnitMesh(unit.State, mesh);
      }
    }
  }
}
