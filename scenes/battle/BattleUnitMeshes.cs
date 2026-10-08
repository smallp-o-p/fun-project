using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System.Collections.Generic;
using System;

/// <summary>Presents alive units as capsule meshes and registers them with the playback director.</summary>
public sealed partial class BattleUnitMeshes : Node3D
{
  public void Initialize(BattleRuntime runtime, EventPlaybackDirector director, Faction playerFaction)
  {
    List<(BattleUnitState Unit, Vector3I Tile, bool Player)> placements = [];
    using (var scope = runtime.TryAcquireQueryScope().Match(Right: value => value, Left: failure => throw new InvalidOperationException($"Battle access failed: {failure}.")))
    {
      foreach (var faction in runtime.Query(new GetGlobalFactionTurnOrderQuery()))
        foreach (var unit in runtime.Query(new GetFactionAliveUnits(faction)))
          placements.Add((unit.State, unit.Position.Raw, faction == playerFaction));
    }
    var capsule = new CapsuleMesh { Radius = 0.3f, Height = 1.0f };
    var playerMaterial = new StandardMaterial3D { AlbedoColor = Colors.SteelBlue };
    var enemyMaterial = new StandardMaterial3D { AlbedoColor = Colors.IndianRed };
    foreach (var placement in placements)
    {
      var mesh = new MeshInstance3D
      {
        Mesh = capsule,
        MaterialOverride = placement.Player ? playerMaterial : enemyMaterial,
        Position = BoardCoordinates.TileToWorldCenter(placement.Tile) + new Vector3(0f, 0.5f, 0f),
      };
      AddChild(mesh);
      director.RegisterUnitMesh(placement.Unit, mesh);
    }
  }
}
