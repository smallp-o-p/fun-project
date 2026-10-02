using Godot;
using System;
using Cell = Godot.Vector3I;

/// <summary>Reusable scenery; the map export bakes its footprint into terrain data.</summary>
[Tool, GlobalClass]
public partial class BattlePropAuthoring : Node3D
{
  [Export] public Godot.Collections.Array<Cell> Footprint { get; set; } = [Cell.Zero];
  [Export] public bool BlocksMovement { get; set; }
  [Export] public bool BlocksLineOfSight { get; set; }
  [Export(PropertyHint.Range, "0,100")] public int CoverNorth { get; set; }
  [Export(PropertyHint.Range, "0,100")] public int CoverEast { get; set; }
  [Export(PropertyHint.Range, "0,100")] public int CoverSouth { get; set; }
  [Export(PropertyHint.Range, "0,100")] public int CoverWest { get; set; }

  internal static readonly Cell[] Directions = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
  internal int[] Cover => [CoverNorth, CoverEast, CoverSouth, CoverWest];

  [ExportToolButton("Snap to grid")]
  private Callable SnapButton => Callable.From(SnapToGrid);
  [ExportToolButton("Refresh footprint preview")]
  private Callable PreviewButton => Callable.From(RefreshPreview);

  public void SnapToGrid()
  {
    var map = GetParent() as BattleMapAuthoring
      ?? throw new InvalidOperationException("Place props directly beneath BattleMapAuthoring.");
    var column = map.LocalToMap(Position);
    float distance = float.PositiveInfinity;
    Vector3? nearest = null;
    foreach (var cell in map.GetUsedCells())
    {
      var ground = map.Palette!.Brushes[map.MeshLibrary.GetItemName(map.GetCellItem(cell))];
      if (cell.X != column.X || cell.Z != column.Z || !ground.Walkable) continue;
      var surface = map.MapToLocal(cell) + Vector3.Up * ground.GroundSurfaceOffset;
      if (Mathf.Abs(Position.Y - surface.Y) >= distance) continue;
      nearest = surface;
      distance = Mathf.Abs(Position.Y - surface.Y);
    }
    Position = nearest ?? throw new InvalidOperationException($"{Name}: no supporting ground in this column.");
    Rotation = new(0, Mathf.Round(Rotation.Y / (Mathf.Pi / 2)) * Mathf.Pi / 2, 0);
    Scale = Vector3.One;
  }

  internal Cell Rotate(Cell cell) => (Cell)(Basis * (Vector3)cell).Round();

  public override void _Ready()
  {
    if (Engine.IsEditorHint()) RefreshPreview();
  }

  public void RefreshPreview()
  {
    GetNodeOrNull<MeshInstance3D>("FootprintPreview")?.Free();
    if (Footprint.Count == 0) return;
    var mesh = new ImmediateMesh();
    mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, new StandardMaterial3D
    {
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
      AlbedoColor = Colors.LimeGreen,
      NoDepthTest = true
    });
    foreach (var cell in Footprint)
    {
      for (int side = 0; side < 4; side++)
      {
        var direction = (Vector3)Directions[side];
        var center = (Vector3)cell + Vector3.Up * 0.025f + direction * 0.5f;
        var tangent = direction.Cross(Vector3.Up) * 0.5f;
        mesh.SurfaceAddVertex(center - tangent);
        mesh.SurfaceAddVertex(center + tangent);
        if (Cover[side] <= 0 || Footprint.Contains(cell + Directions[side])) continue;
        mesh.SurfaceAddVertex(center);
        mesh.SurfaceAddVertex(center + direction * 0.35f);
      }
    }
    mesh.SurfaceEnd();
    AddChild(new MeshInstance3D
    {
      Name = "FootprintPreview",
      Mesh = mesh,
      CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
    }, false, InternalMode.Back);
  }
}
