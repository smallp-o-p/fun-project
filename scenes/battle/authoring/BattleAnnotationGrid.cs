using Godot;
using System;
using Cell = Godot.Vector3I;

// A fixed-layout authoring aid inside one reusable asset, never runtime map geometry.
[Tool, GlobalClass]
public partial class BattleAnnotationGrid : GridMap
{
  [Export] public BattleFootprintData Annotations { get; set; } = new();
  [Export] public Godot.Collections.Dictionary<Cell, Vector2I> Baseline { get; set; } = [];

  public BattleAnnotationGrid()
  {
    // PackedScene restores native properties before attaching this script.
    if (MeshLibrary is not null) return;
    CellSize = Vector3.One;
    MeshLibrary = new MeshLibrary();
    MeshLibrary.CreateItem(0);
    MeshLibrary.SetItemName(0, "Annotation box");
    MeshLibrary.SetItemMesh(0, new BoxMesh
    {
      Size = Vector3.One * 0.96f,
      Material = new StandardMaterial3D
      {
        AlbedoColor = new Color(0.15f, 0.85f, 0.75f, 0.28f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        RenderPriority = 1,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
      }
    });
  }

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsString() is nameof(Annotations) or nameof(Baseline))
      property["usage"] = (int)PropertyUsageFlags.Storage;
  }

  public Godot.Collections.Dictionary<Cell, Vector2I> CaptureLayout()
  {
    Godot.Collections.Dictionary<Cell, Vector2I> result = [];
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

  public bool TrySelectedCell(Godot.Collections.Array selected, out Cell cell)
  {
    cell = selected.Count == 1 ? selected[0].AsVector3I() : default;
    return selected.Count == 1 && GetCellItem(cell) == 0;
  }

  public string AuthoringProblem(Vector3 expectedSize)
  {
    if (GetParent() is not (BattleProp or BattleFloor)) return "Put the annotation GridMap directly inside a BattleProp or BattleFloor asset.";
    if (!Transform.IsEqualApprox(Transform3D.Identity) || TopLevel || !CellCenterX || !CellCenterY || !CellCenterZ || !Mathf.IsEqualApprox(CellScale, 1))
      return "Annotation boxes need an identity transform, centered cells and Cell Scale 1.";
    if (!CellSize.IsEqualApprox(expectedSize)) return $"Cell Size must match the grid metrics {expectedSize}. Standalone assets use unit metrics.";
    if (MeshLibrary is null || MeshLibrary.GetItemList().Length != 1 || MeshLibrary.GetItemList()[0] != 0)
      return "Use the single annotation-box marker (item 0).";
    if (MeshLibrary.GetItemMesh(0) is not BoxMesh marker || !marker.Size.IsEqualApprox(CellSize * 0.96f) ||
        !MeshLibrary.GetItemMeshTransform(0).IsEqualApprox(Transform3D.Identity))
      return "The marker must be a centered box sized to 96% of Cell Size, with an identity mesh transform.";
    foreach (var cell in GetUsedCells())
    {
      if (GetCellItem(cell) != 0) return "Only annotation-box marker item 0 is supported.";
      var basis = GetCellItemBasis(cell);
      bool upright = false;
      for (int turn = 0; turn < 4; turn++) upright |= basis.IsEqualApprox(new Basis(Vector3.Up, turn * Mathf.Pi / 2));
      if (!upright) return $"Box {cell} has pitch or roll. Only upright Y quarter-turns are supported.";
    }
    if (Baseline.Count > 0 && !LayoutMatches())
      return "Layout changed: undo the paint/move/rotation, or reset annotations. Editing and export are blocked; flags do not follow moved boxes.";
    return "";
  }

  public BattleFootprintData BuildFootprint(Vector3 size)
  {
    var problem = AuthoringProblem(size);
    if (problem != "") throw new InvalidOperationException($"{Name}: {problem}");
    if (GetUsedCells().Count > 0 && Baseline.Count == 0)
      throw new InvalidOperationException($"{Name}: inspect a selected box to begin annotations before exporting.");
    var result = new BattleFootprintData();
    foreach (var cell in GetUsedCells())
      result.Cells[cell] = Annotations.Cells.TryGetValue(cell, out var data) ? data : new BattleFootprintCellData();
    return result;
  }
}
