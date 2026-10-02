using FunProject.Battle;
using Godot;
using System;
using Godot.Collections;
using Cell = Godot.Vector3I;

/// <summary>
/// A tool to create maps to be used in a tactical battle via the Godot editor.
/// All tiles should have X, Y, Z >= 0.
/// </summary>
[Tool]
public partial class BattleMapAuthoring : GridMap
{
  private BattleTilePalette? _palette;
  [Export]
  public BattleTilePalette? Palette
  {
    get => _palette;
    set
    {
      _palette = value;
      NotifyPropertyListChanged();
    }
  }

  [Export(PropertyHint.File, "*.tscn,*.scn")]
  public string TargetPath { get; set; } = "res://resources/maps/untitled_map.tscn";

  [ExportToolButton("Export BattleMap")]
  private Callable BakeButton => Callable.From(Bake);

  public override void _ValidateProperty(Dictionary property)
  {
    if (property["name"].AsStringName() == PropertyName.Palette)
    {
      MeshLibrary = Palette?.MeshLibrary;
    }
  }

  private void Bake()
  {
    using var scene = BuildScene();
    var saveRes = ResourceSaver.Save(scene, TargetPath);
    if (saveRes != Error.Ok)
      throw new InvalidOperationException($"Failed to save map at {TargetPath}");
    GD.Print($"Successfully saved map to {TargetPath}");
  }

  public PackedScene BuildScene()
  {
    if (Palette is null || MeshLibrary is null)
      throw new InvalidOperationException("Palette and mesh library are required.");
    var cells = GetPaintedCells();

    if (cells.Count == 0)
    {
      throw new InvalidOperationException("BattleMapAuthoring: no painted cells found on the GridMap.");
    }

    BattleMapData data = BuildMap(cells);

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
      Scale = Scale,
      Name = "Map"
    };

    foreach (var c in GetUsedCells())
    {
      var item = GetCellItem(c);
      var orientation = GetCellItemOrientation(c);
      map.SetCellItem(c, item, orientation);
    }

    foreach (var child in GetChildren())
    {
      if (child is not BattlePropAuthoring prop) continue;
      var visual = new Node3D { Name = prop.Name, Transform = prop.Transform };
      foreach (var node in prop.GetChildren()) visual.AddChild(node.Duplicate());
      map.AddChild(visual);
      OwnVisual(visual, map);
    }
    var scene = new PackedScene();
    var res = scene.Pack(map);
    map.Free();
    if (res != Error.Ok)
      throw new InvalidOperationException("Failed to pack map");
    return scene;
  }

  private Dictionary<Godot.Vector3I, BattleMapTileData> GetPaintedCells()
  {
    var painted = new Dictionary<Godot.Vector3I, BattleMapTileData>();

    foreach (Godot.Vector3I cell in GetUsedCells())
    {
      if (cell.X < 0 || cell.Y < 0 || cell.Z < 0)
        throw new InvalidOperationException("Map has cells that have negative dimensions.");

      var itemId = GetCellItem(cell);
      if (itemId == InvalidCellItem)
        continue;

      StringName itemName = MeshLibrary.GetItemName(itemId);

      if (Palette!.Brushes.TryGetValue(itemName, out var value))
        painted[cell] = (BattleMapTileData)value.Duplicate();
      else
        throw new InvalidOperationException($"There is no data associated with tile {itemName}");
    }

    return painted;
  }

  private BattleMapData BuildMap(Dictionary<Cell, BattleMapTileData> cells)
  {
    Cell dimensions = Cell.Zero;
    foreach (var (cell, tile) in cells)
    {
      if (tile.SpawnFactionSlot >= 0 && !tile.Walkable)
        throw new InvalidOperationException($"Spawn at {cell} requires walkable ground.");
      dimensions = new(Math.Max(dimensions.X, cell.X + 1), Math.Max(dimensions.Y, cell.Y + 1), Math.Max(dimensions.Z, cell.Z + 1));
    }

    var props = new SysColGeneric.List<(BattlePropAuthoring Prop, Cell Anchor)>();
    foreach (var child in FindChildren("*", "", true, false))
    {
      if (child is not BattlePropAuthoring prop) continue;
      if (prop.GetParent() != this || !Transform.IsEqualApprox(Transform3D.Identity) ||
          !CellSize.IsEqualApprox(Vector3.One) || !CellCenterX || CellCenterY || !CellCenterZ)
        throw new InvalidOperationException("Props require direct parenting to an identity, unit-cell GridMap (X/Z centered, Y uncentered).");
      var footprint = new SysColGeneric.HashSet<Cell>(prop.Footprint);
      if (!footprint.Contains(Cell.Zero) || footprint.Count != prop.Footprint.Count)
        throw new InvalidOperationException($"{prop.Name}: footprint must include the origin without duplicate cells.");
      foreach (var amount in prop.Cover)
        if (amount < 0 || amount > 100)
          throw new InvalidOperationException($"{prop.Name}: cover must be between 0 and 100.");
      // The snapped scene transform is the only placement state; find its actual support surface.
      Cell? anchor = null;
      foreach (var (cell, tile) in cells)
        if ((MapToLocal(cell) + Vector3.Up * tile.GroundSurfaceOffset).IsEqualApprox(prop.Position)) anchor = cell;
      if (anchor is null || !prop.Basis.IsEqualApprox(new Basis(Vector3.Up, Mathf.Round(prop.Rotation.Y / (Mathf.Pi / 2)) * Mathf.Pi / 2)))
        throw new InvalidOperationException($"{prop.Name}: use Snap to grid; scale and tilt are unsupported.");
      float height = cells[anchor.Value].GroundSurfaceOffset;
      foreach (var local in footprint)
      {
        var cell = anchor.Value + prop.Rotate(local);
        if (local.Y != 0 || !cells.TryGetValue(cell, out var tile) ||
            !Palette!.Brushes[MeshLibrary.GetItemName(GetCellItem(cell))].Walkable || tile.GroundSurfaceOffset != height)
          throw new InvalidOperationException($"{prop.Name}: footprint requires level, walkable ground at {cell}.");
        if (prop.BlocksMovement && (!tile.Walkable || tile.SpawnFactionSlot >= 0))
          throw new InvalidOperationException($"{prop.Name}: movement footprint overlaps another prop or spawn at {cell}.");
        if (prop.BlocksMovement) tile.Walkable = false;
        tile.BlocksLineOfSight |= prop.BlocksLineOfSight;
      }
      props.Add((prop, anchor.Value));
    }
    // Stamp cover after movement so order cannot give cover to a later blocked tile.
    foreach (var (prop, anchor) in props)
      foreach (var local in prop.Footprint)
        for (int side = 0; side < 4; side++)
        {
          var direction = BattlePropAuthoring.Directions[side];
          if (prop.Footprint.Contains(local + direction)) continue;
          var outward = prop.Rotate(direction);
          var neighbor = anchor + prop.Rotate(local) + outward;
          if (!cells.TryGetValue(neighbor, out var tile) || !tile.Walkable) continue;
          int amount = prop.Cover[side];
          if (outward.Z == 1) tile.CoverNorth = Math.Max(tile.CoverNorth, amount);
          if (outward.X == -1) tile.CoverEast = Math.Max(tile.CoverEast, amount);
          if (outward.Z == -1) tile.CoverSouth = Math.Max(tile.CoverSouth, amount);
          if (outward.X == 1) tile.CoverWest = Math.Max(tile.CoverWest, amount);
        }
    return new BattleMapData { Dimensions = dimensions, Tiles = cells };
  }

  private static void OwnVisual(Node node, Node owner)
  {
    node.Owner = owner;
    // Layer 1 remains terrain-only for tactical ground picking.
    switch (node)
    {
      case CollisionObject3D collider: collider.CollisionLayer = 2; break;
      case CsgShape3D csg: csg.CollisionLayer = 2; break;
      case GridMap grid: grid.CollisionLayer = 2; break;
    }
    foreach (var child in node.GetChildren()) OwnVisual(child, owner);
  }
}
