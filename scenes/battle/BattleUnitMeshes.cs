using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System.Collections.Generic;

/// <summary>Presents alive units as capsule meshes and registers them with the playback director.</summary>
public sealed partial class BattleUnitMeshes : Node3D
{
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
        director.RegisterUnitMesh(unit.State, mesh);
      }
    }
  }
}
