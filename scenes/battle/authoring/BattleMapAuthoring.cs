using FunProject.Battle;
using Godot;
using System;
using Godot.Collections;
using Cell = Godot.Vector3I;

/// <summary>Paint terrain, place props, then export one runtime BattleMap scene.</summary>
[Tool]
public partial class BattleMapAuthoring : GridMap
{
  private BattleTilePalette? _palette;
  [Export]
  public BattleTilePalette? Palette
  {
    get => _palette;
    set { _palette = value; MeshLibrary = value?.MeshLibrary; }
  }

  [Export(PropertyHint.File, "*.tscn,*.scn")]
  public string TargetPath { get; set; } = "res://resources/maps/untitled_map.tscn";

  [ExportToolButton("Export BattleMap")]
  private Callable BakeButton => Callable.From(Bake);

  private void Bake()
  {
    try
    {
      if (!TargetPath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) && !TargetPath.EndsWith(".scn", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Target Path must be a .tscn or .scn scene.");
      using var scene = BuildScene();
      var result = ResourceSaver.Save(scene, TargetPath);
      if (result != Error.Ok)
        throw new InvalidOperationException($"Failed to save map at {TargetPath}: {result}");
      GD.Print($"Successfully saved map to {TargetPath}");
    }
    catch (InvalidOperationException error) { GD.PushError(error.Message); }
  }

  public PackedScene BuildScene()
  {
    ValidateGrid();
    var cells = GetPaintedCells();
    if (cells.Count == 0) throw new InvalidOperationException("BattleMapAuthoring has no painted cells.");
    var placements = new SysColGeneric.List<BattlePropPlacement>();
    var props = new SysColGeneric.List<BattlePropAuthoring>();
    foreach (var child in FindChildren("*", "", true, false))
    {
      if (child is not BattlePropAuthoring prop) continue;
      if (prop.GetParent() != this)
        throw new InvalidOperationException($"{prop.Name}: props must be direct children of the authoring GridMap.");
      placements.Add(prop.GetPlacement());
      props.Add(prop);
    }
    var data = BattleMapBaker.Bake(cells, placements);
    var map = new BattleMap
    {
      MeshLibrary = MeshLibrary,
      MapData = data,
      UsedPalette = Palette!,
      CellOctantSize = CellOctantSize,
      CellSize = CellSize,
      CellCenterX = CellCenterX,
      CellCenterY = CellCenterY,
      CellCenterZ = CellCenterZ,
      CollisionLayer = 1,
      Name = "Map"
    };
    try
    {
      foreach (var cell in GetUsedCells())
        map.SetCellItem(cell, GetCellItem(cell), GetCellItemOrientation(cell));
      foreach (var prop in props)
      {
        var visual = prop.CopyVisual();
        map.AddChild(visual);
        OwnVisual(visual, map);
      }
      var scene = new PackedScene();
      if (scene.Pack(map) != Error.Ok)
        throw new InvalidOperationException("Failed to pack map.");
      return scene;
    }
    finally { map.Free(); }
  }

  private static void OwnVisual(Node node, Node owner)
  {
    node.Owner = owner;
    // Tactical picking sees terrain only; visual collisions never change board logic.
    switch (node)
    {
      case CollisionObject3D collider: collider.CollisionLayer = 2; break;
      case CsgShape3D csg: csg.CollisionLayer = 2; break;
      case GridMap grid: grid.CollisionLayer = 2; break;
    }
    foreach (var child in node.GetChildren()) OwnVisual(child, owner);
  }

  public void ValidateGrid()
  {
    if (Palette is null || MeshLibrary is null)
      throw new InvalidOperationException("BattleMapAuthoring requires a palette and mesh library.");
    if (!Transform.IsEqualApprox(Transform3D.Identity) || !CellSize.IsEqualApprox(Vector3.One) || !CellCenterX || CellCenterY || !CellCenterZ)
      throw new InvalidOperationException("BattleMapAuthoring requires identity transform, unit cells, X/Z centered and Y uncentered.");
  }

  public Dictionary<Cell, BattleMapTileData> GetPaintedCells()
  {
    if (Palette is null || MeshLibrary is null)
      throw new InvalidOperationException("BattleMapAuthoring requires a palette and mesh library.");
    var cells = new Dictionary<Cell, BattleMapTileData>();
    foreach (var cell in GetUsedCells())
    {
      var item = GetCellItem(cell);
      var name = MeshLibrary.GetItemName(item);
      if (!Palette.Brushes.TryGetValue(name, out var brush))
        throw new InvalidOperationException($"No tile data for {name} at {cell}.");
      cells[cell] = brush;
    }
    return cells;
  }

  public Vector3 GroundPosition(Cell anchor)
  {
    var cells = GetPaintedCells();
    if (!cells.TryGetValue(anchor, out var support))
      throw new InvalidOperationException($"No ground beneath anchor {anchor}.");
    return MapToLocal(anchor) + Vector3.Up * support.GroundSurfaceOffset;
  }
}
