using Godot;
using System.Collections.Generic;

// Renders a flat translucent marker over each reachable tile. One shared mesh/material; markers are
// child MeshInstance3D nodes recreated on each Show.
public sealed partial class ReachableTileHighlighter : Node3D
{
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
    var mesh = new PlaneMesh { Size = new Vector2(0.9f, 0.9f) };

    foreach (Vector3I tile in tiles)
    {
      var marker = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
      Vector3 center = BoardCoordinates.TileToWorldCenter(tile);
      marker.Position = new Vector3(center.X, center.Y + 0.02f, center.Z);
      AddChild(marker);
    }
  }

  public void Clear()
  {
    foreach (Node child in GetChildren())
      child.QueueFree();
  }
}
