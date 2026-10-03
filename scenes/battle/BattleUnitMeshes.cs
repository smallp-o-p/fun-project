using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System.Collections.Generic;

/// <summary>Presents alive units as capsule meshes and registers them with the playback director.</summary>
public sealed partial class BattleUnitMeshes : Node3D
{
  public BoardCoordinates Coordinates { get; set; } = BoardCoordinates.UnitGrid;

  public void Initialize(BattleRuntime runtime, EventPlaybackDirector director, Faction playerFaction)
  {
    var capsule = new CapsuleMesh { Radius = Mathf.Min(0.3f * Coordinates.CellSize.X, Coordinates.CellSize.Y / 2), Height = Coordinates.CellSize.Y };

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
        };
        AddChild(mesh);
        mesh.GlobalTransform = new Transform3D(Coordinates.MapTransform.Basis, Coordinates.TileToWorldVolumeCenter(tile));
        director.RegisterUnitMesh(unit.State, mesh);
      }
    }
  }
}
