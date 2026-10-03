using Godot;
using System.Collections.Generic;

// Renders a flat translucent marker over each reachable tile. One shared mesh/material; markers are
// child MeshInstance3D nodes recreated on each Show.
public sealed partial class ReachableTileHighlighter : Node3D
{
  public BoardCoordinates Coordinates { get; set; } = BoardCoordinates.UnitGrid;

  [Export] public Color HighlightColor { get; set; } = new(0.2f, 0.8f, 1.0f, 0.35f);

  public void Show(IEnumerable<Vector3I> tiles)
  {
    Clear();
    var material = new StandardMaterial3D
    {
      AlbedoColor = HighlightColor,
      Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    var mesh = new PlaneMesh { Size = Vector2.One * (0.9f * Coordinates.CellSize.X) };

    foreach (Vector3I tile in tiles)
    {
      var marker = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
      Vector3 center = Coordinates.TileToWorldCenter(tile);
      AddChild(marker);
      marker.GlobalTransform = new Transform3D(Coordinates.MapTransform.Basis, center + Coordinates.Up * (0.02f * Coordinates.CellSize.Y));
    }
  }

  public void Clear()
  {
    foreach (Node child in GetChildren())
      child.QueueFree();
  }
}
