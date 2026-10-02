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
  private static readonly Cell[] Directions = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
  [Export] public GridMap? Props { get; set; }
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
    var cells = GetPaintedCells(this);

    if (cells.Count == 0)
    {
      throw new InvalidOperationException("BattleMapAuthoring: no painted cells found on the GridMap.");
    }

    BattleMapData data = BuildMap(cells);

    var map = new BattleMap
    {
      MapData = data,
      UsedPalette = Palette!,
      Scale = Scale,
      Name = "Map"
    };

    CopyGrid(this, map);

    if (Props is not null)
    {
      var props = new GridMap
      {
        Name = "Props",
        Transform = Props.Transform,
        CollisionLayer = 2
      };
      CopyGrid(Props, props);
      map.AddChild(props);
      props.Owner = map;
    }
    var scene = new PackedScene();
    var res = scene.Pack(map);
    map.Free();
    if (res != Error.Ok)
      throw new InvalidOperationException("Failed to pack map");
    return scene;
  }

  private static void CopyGrid(GridMap source, GridMap target)
  {
    target.MeshLibrary = source.MeshLibrary;
    target.CellOctantSize = source.CellOctantSize;
    target.CellSize = source.CellSize;
    target.CellCenterX = source.CellCenterX;
    target.CellCenterY = source.CellCenterY;
    target.CellCenterZ = source.CellCenterZ;
    foreach (var cell in source.GetUsedCells())
      target.SetCellItem(cell, source.GetCellItem(cell), source.GetCellItemOrientation(cell));
  }

  private Dictionary<Godot.Vector3I, BattleMapTileData> GetPaintedCells(GridMap source)
  {
    var painted = new Dictionary<Godot.Vector3I, BattleMapTileData>();

    foreach (Godot.Vector3I cell in source.GetUsedCells())
    {
      if (cell.X < 0 || cell.Y < 0 || cell.Z < 0)
        throw new InvalidOperationException("Map has cells that have negative dimensions.");

      var itemId = source.GetCellItem(cell);
      if (itemId == InvalidCellItem)
        continue;

      StringName itemName = source.MeshLibrary.GetItemName(itemId);

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

    if (Props is null) return new BattleMapData { Dimensions = dimensions, Tiles = cells };
    if (Props.GetParent() != this || Props.MeshLibrary != MeshLibrary ||
        !Transform.IsEqualApprox(Transform3D.Identity) || !Props.Basis.IsEqualApprox(Basis.Identity) ||
        !Props.Position.IsEqualApprox(Vector3.Up * Props.Position.Y) ||
        !CellSize.IsEqualApprox(Vector3.One) || !Props.CellSize.IsEqualApprox(Vector3.One) ||
        !Mathf.IsEqualApprox(CellScale, 1) || !Mathf.IsEqualApprox(Props.CellScale, 1) ||
        !CellCenterX || CellCenterY || !CellCenterZ || !Props.CellCenterX || Props.CellCenterY || !Props.CellCenterZ)
      throw new InvalidOperationException("Props must be a direct, unscaled GridMap using this palette, unit cells, X/Z centered, Y uncentered, and only a vertical surface offset.");

    var props = new SysColGeneric.List<(BattleMapTileData Brush, Cell Anchor, Basis Rotation)>();
    foreach (var (anchor, prop) in GetPaintedCells(Props))
    {
      var name = $"Prop at {anchor}";
      var rotation = Props.GetBasisWithOrthogonalIndex(Props.GetCellItemOrientation(anchor));
      if (!rotation.Y.IsEqualApprox(Vector3.Up))
        throw new InvalidOperationException($"{name}: props only support upright Y quarter-turns.");
      var footprint = new SysColGeneric.HashSet<Cell>(prop.PropFootprint);
      if (!footprint.Contains(Cell.Zero) || footprint.Count != prop.PropFootprint.Count)
        throw new InvalidOperationException($"{name}: footprint must include the origin without duplicate cells.");
      foreach (int amount in new[] { prop.PropCoverNorth, prop.PropCoverEast, prop.PropCoverSouth, prop.PropCoverWest })
        if (amount < 0 || amount > 100)
          throw new InvalidOperationException($"{name}: cover must be between 0 and 100.");
      foreach (var local in footprint)
      {
        var cell = anchor + (Cell)(rotation * (Vector3)local).Round();
        if (local.Y != 0 || !cells.TryGetValue(cell, out var tile) ||
            !Palette!.Brushes[MeshLibrary.GetItemName(GetCellItem(cell))].Walkable ||
            !Mathf.IsEqualApprox(tile.GroundSurfaceOffset, Props.Position.Y))
          throw new InvalidOperationException($"{name}: footprint requires walkable ground matching the Props layer's surface offset at {cell}.");
        if (prop.PropBlocksMovement && (!tile.Walkable || tile.SpawnFactionSlot >= 0))
          throw new InvalidOperationException($"{name}: movement footprint overlaps another prop or spawn at {cell}.");
        if (prop.PropBlocksMovement) tile.Walkable = false;
        tile.BlocksLineOfSight |= prop.BlocksLineOfSight;
      }
      props.Add((prop, anchor, rotation));
    }
    // Stamp cover after movement so order cannot give cover to a later blocked tile.
    foreach (var (prop, anchor, rotation) in props)
    {
      int[] cover = [prop.PropCoverNorth, prop.PropCoverEast, prop.PropCoverSouth, prop.PropCoverWest];
      foreach (var local in prop.PropFootprint)
        for (int side = 0; side < 4; side++)
        {
          var direction = Directions[side];
          if (prop.PropFootprint.Contains(local + direction)) continue;
          var outward = (Cell)(rotation * (Vector3)direction).Round();
          var neighbor = anchor + (Cell)(rotation * (Vector3)local).Round() + outward;
          if (!cells.TryGetValue(neighbor, out var tile) || !tile.Walkable) continue;
          int amount = cover[side];
          if (outward.Z == 1) tile.CoverNorth = Math.Max(tile.CoverNorth, amount);
          if (outward.X == -1) tile.CoverEast = Math.Max(tile.CoverEast, amount);
          if (outward.Z == -1) tile.CoverSouth = Math.Max(tile.CoverSouth, amount);
          if (outward.X == 1) tile.CoverWest = Math.Max(tile.CoverWest, amount);
        }
    }
    return new BattleMapData { Dimensions = dimensions, Tiles = cells };
  }

}
