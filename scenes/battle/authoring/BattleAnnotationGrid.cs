using Godot;
using System;

// A fixed-layout authoring aid inside one reusable asset, never runtime map geometry.
[Tool, GlobalClass]
public partial class BattleAnnotationGrid : GridMap
{
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Annotations { get; set; } = [];
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, Vector2I> Baseline { get; set; } = [];

  public BattleAnnotationGrid()
  {
    // PackedScene restores native properties before attaching this script.
    if (MeshLibrary is not null) return;
    CellSize = Vector3.One;
    MeshLibrary = new MeshLibrary();
    MeshLibrary.CreateItem(0);
    MeshLibrary.SetItemName(0, "Annotation box");
    const float halfSize = 0.48f;
    Vector3[] edges =
    [
      new(-halfSize, -halfSize, -halfSize), new(halfSize, -halfSize, -halfSize),
      new(halfSize, -halfSize, -halfSize), new(halfSize, -halfSize, halfSize),
      new(halfSize, -halfSize, halfSize), new(-halfSize, -halfSize, halfSize),
      new(-halfSize, -halfSize, halfSize), new(-halfSize, -halfSize, -halfSize),
      new(-halfSize, halfSize, -halfSize), new(halfSize, halfSize, -halfSize),
      new(halfSize, halfSize, -halfSize), new(halfSize, halfSize, halfSize),
      new(halfSize, halfSize, halfSize), new(-halfSize, halfSize, halfSize),
      new(-halfSize, halfSize, halfSize), new(-halfSize, halfSize, -halfSize),
      new(-halfSize, -halfSize, -halfSize), new(-halfSize, halfSize, -halfSize),
      new(halfSize, -halfSize, -halfSize), new(halfSize, halfSize, -halfSize),
      new(halfSize, -halfSize, halfSize), new(halfSize, halfSize, halfSize),
      new(-halfSize, -halfSize, halfSize), new(-halfSize, halfSize, halfSize)
    ];
    MeshLibrary.SetItemMesh(0, CreateMarker(edges, new Color(0.15f, 0.85f, 0.75f, 1)));
    Vector3[] floorEdges = new Vector3[8];
    for (int i = 0; i < floorEdges.Length; i++) floorEdges[i] = new(edges[i].X, -0.5f, edges[i].Z);
    MeshLibrary.CreateItem(1);
    MeshLibrary.SetItemName(1, "Floor");
    MeshLibrary.SetItemMesh(1, CreateMarker(floorEdges, new Color(0.3f, 0.9f, 0.35f, 1)));
    MeshLibrary.CreateItem(2);
    MeshLibrary.SetItemName(2, "Solid");
    MeshLibrary.SetItemMesh(2, CreateMarker(edges, new Color(1, 0.55f, 0.15f, 1)));
  }

  private static ArrayMesh CreateMarker(Vector3[] edges, Color color)
  {
    Godot.Collections.Array arrays = [];
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = edges;
    var marker = new ArrayMesh();
    marker.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
    marker.SurfaceSetMaterial(0, new StandardMaterial3D
    {
      AlbedoColor = color,
      NoDepthTest = true,
      RenderPriority = 1,
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
    });
    return marker;
  }

  public override void _EnterTree()
  {
    if (!Engine.IsEditorHint()) Hide();
  }

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsString() is nameof(Annotations) or nameof(Baseline))
      property["usage"] = (int)PropertyUsageFlags.Storage;
  }

  public Godot.Collections.Dictionary<Godot.Vector3I, Vector2I> CaptureLayout()
  {
    Godot.Collections.Dictionary<Godot.Vector3I, Vector2I> result = [];
    foreach (var cell in GetUsedCells()) result[cell] = new(GetCellItem(cell), GetCellItemOrientation(cell));
    return result;
  }

  public bool LayoutMatches()
  {
    if (Baseline.Count != GetUsedCells().Count) return false;
    foreach (var (cell, saved) in Baseline)
      if (GetCellItem(cell) != saved.X || GetCellItemOrientation(cell) != saved.Y) return false;
    return true;
  }

  public bool TrySelectedCell(Godot.Collections.Array selected, out Godot.Vector3I cell)
  {
    cell = selected.Count == 1 ? selected[0].AsVector3I() : default;
    return selected.Count == 1 && GetCellItem(cell) is 0 or 1 or 2;
  }

  internal BattleFootprintData CreateDefaultAnnotation(Godot.Vector3I cell) => GetCellItem(cell) switch
  {
    0 => new(),
    1 => new() { HasFloor = true },
    2 => new() { BlocksMovement = true, BlocksLineOfSight = true },
    _ => throw new InvalidOperationException($"Cell {cell} needs a supported annotation marker.")
  };

  public Option<string> Validate(bool requireAnnotations = true)
  {
    if (GetParent() is not Node3D || GetParent() is BattleMap)
      return "Put the annotation GridMap directly inside a reusable Node3D asset, not the map root.";
    if (!Transform.IsEqualApprox(Transform3D.Identity) || TopLevel || !CellCenterX || !CellCenterY || !CellCenterZ || !Mathf.IsEqualApprox(CellScale, 1))
      return "Annotation boxes need an identity transform, centered cells and Cell Scale 1.";
    if (!CellSize.IsEqualApprox(Vector3.One)) return "Annotation boxes require unit Cell Size.";
    var items = MeshLibrary?.GetItemList() ?? [];
    if (System.Array.IndexOf(items, 0) < 0)
      return "Keep the annotation-box marker (item 0) in the palette; Floor (1) and Solid (2) are optional shortcuts.";
    foreach (int item in items)
    {
      if (item is not (0 or 1 or 2)) return "Use only Annotation box (0), Floor (1), or Solid (2).";
      bool floor = item == 1;
      var position = floor ? new Vector3(-0.48f, -0.5f, -0.48f) : -Vector3.One * 0.48f;
      // ArrayMesh pads a flat surface's AABB to Godot's minimum thickness.
      var size = floor ? new Vector3(0.96f, 0.00001f, 0.96f) : Vector3.One * 0.96f;
      if (MeshLibrary!.GetItemMesh(item) is not ArrayMesh marker || marker.GetSurfaceCount() != 1 ||
          marker.SurfaceGetPrimitiveType(0) != Mesh.PrimitiveType.Lines || marker.SurfaceGetArrayLen(0) != (floor ? 8 : 24) ||
          marker.SurfaceGetArrayIndexLen(0) != 0 || !marker.GetAabb().Position.IsEqualApprox(position) ||
          !marker.GetAabb().Size.IsEqualApprox(size) ||
          !MeshLibrary.GetItemMeshTransform(item).IsEqualApprox(Transform3D.Identity))
        return "Use the inset box outlines and bottom-face Floor outline, with identity mesh transforms.";
    }
    bool needsAnnotation = false;
    foreach (var cell in GetUsedCells())
    {
      int item = GetCellItem(cell);
      if (System.Array.IndexOf(items, item) < 0) return $"Cell {cell} needs an annotation marker from this palette.";
      needsAnnotation |= item == 0;
      if (!BattleMapAuthoring.IsUpright(GetCellItemBasis(cell)))
        return $"Box {cell} has pitch or roll. Only upright Y quarter-turns are supported.";
    }
    if (Baseline.Count > 0 && !LayoutMatches())
      return "Layout changed: undo the paint/move/rotation, or reset annotations. Editing and baking are blocked; flags do not follow moved boxes.";
    if (requireAnnotations && needsAnnotation && Baseline.Count == 0)
      return "Inspect a selected box to begin annotations before baking.";
    return None;
  }

  public Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> BuildFootprint()
  {
    Validate().IfSome(problem => throw new InvalidOperationException($"{Name}: {problem}"));
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> result = [];
    foreach (var cell in GetUsedCells())
      result[cell] = Annotations.TryGetValue(cell, out var data) ? data : CreateDefaultAnnotation(cell);
    return result;
  }
}
