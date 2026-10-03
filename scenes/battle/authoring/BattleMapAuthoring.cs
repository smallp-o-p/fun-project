using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;
using Cell = Godot.Vector3I;

[Tool]
public partial class BattleMapAuthoring : Node3D
{
  private static readonly Cell[] Directions = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
  private static readonly CoverDirections[] Sides = [CoverDirections.North, CoverDirections.East, CoverDirections.South, CoverDirections.West];
  [Export] public Vector3 GridOrigin { get; set; }
  [Export] public float CellWidth { get; set; } = 1;
  [Export] public float LevelHeight { get; set; } = 1;
  [Export(PropertyHint.File, "*.tscn,*.scn")] public string TargetPath { get; set; } = "res://resources/maps/untitled_map.tscn";
  [ExportToolButton("Export BattleMap")] private Callable BakeButton => Callable.From(Bake);

  private void Bake()
  {
    using var packed = BuildScene();
    if (ResourceSaver.Save(packed, TargetPath) != Error.Ok)
      throw new InvalidOperationException($"Could not save map to {TargetPath}.");
    GD.Print($"Saved battle map to {TargetPath}.");
  }

  public PackedScene BuildScene()
  {
    var rootPlacement = WorldTransform(this);
    if (!IsUpright(rootPlacement.Basis) || !rootPlacement.Origin.IsFinite())
      throw new InvalidOperationException("Map root requires finite placement, upright quarter-turn rotation and unit scale.");
    var pieces = new List<(BattleFootprintData, Transform3D)>();
    var rootInverse = rootPlacement.AffineInverse();
    Collect(this, false);
    var metrics = new BattleMapData { GridOrigin = GridOrigin, CellWidth = CellWidth, LevelHeight = LevelHeight };
    var data = BuildMap(pieces, metrics, out var floors);
    var map = new BattleMap { Name = "Map", Transform = rootPlacement, MapData = data };
    try
    {
      foreach (var child in GetChildren())
      {
        bool localize = HasAnnotations(child);
        if (localize)
        {
          var branch = new List<Node> { child };
          branch.AddRange(child.FindChildren("*", "", true, false));
          foreach (var node in branch)
            if (node.UniqueNameInOwner)
              throw new InvalidOperationException($"{node.Name}: annotation export cannot localize scene-unique (%Name) nodes. Use a static visual branch.");
        }
        var copy = localize
          ? child.Duplicate((int)(Node.DuplicateFlags.Signals | Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts))
          : child.Duplicate();
        map.AddChild(copy);
        Prepare(copy, localize);
      }
      var picking = new StaticBody3D { Name = "FloorPicking", CollisionLayer = 1, CollisionMask = 0 };
      map.AddChild(picking);
      picking.Owner = map;
      var grid = new BoardCoordinates(data, Transform3D.Identity);
      var shape = new BoxShape3D { Size = new(CellWidth, 0.02f * LevelHeight, CellWidth) };
      foreach (var cell in floors)
      {
        var collider = new CollisionShape3D
        {
          Shape = shape,
          Position = grid.TileToWorldCenter(new(cell.X, cell.Y, cell.Z)) - Vector3.Up * (0.01f * LevelHeight)
        };
        picking.AddChild(collider);
        collider.Owner = map;
      }
      var packed = new PackedScene();
      if (packed.Pack(map) != Error.Ok) { packed.Dispose(); throw new InvalidOperationException("Could not pack battle map."); }
      return packed;
    }
    finally { map.Free(); }

    void Collect(Node node, bool insideAsset)
    {
      if (node is BattleAnnotationGrid && node.GetParent() is not (BattleProp or BattleFloor))
        throw new InvalidOperationException($"{node.Name}: annotation GridMaps must be direct children of reusable assets.");
      if (node is BattleProp or BattleFloor)
      {
        BattleAnnotationGrid? annotations = null;
        foreach (var child in node.GetChildren())
          if (child is BattleAnnotationGrid grid)
          {
            if (annotations is not null) throw new InvalidOperationException($"{node.Name}: use one annotation GridMap per asset.");
            annotations = grid;
          }
        if (annotations is not null)
          pieces.Add((annotations.BuildFootprint(new(CellWidth, LevelHeight, CellWidth)),
            rootInverse * WorldTransform((Node3D)node)));
        insideAsset = true;
      }
      if (node is GeometryInstance3D or GridMap && !insideAsset)
        throw new InvalidOperationException($"{node.Name}: map geometry must belong to a BattleFloor or BattleProp.");
      foreach (var child in node.GetChildren()) Collect(child, insideAsset);
    }

    void Prepare(Node node, bool localize)
    {
      if (node is BattleAnnotationGrid) { node.Free(); return; }
      // Flatten only annotated export branches. Otherwise an instance source can recreate
      // removed markers, or freshly duplicated children can double up on scene reload.
      if (localize) { node.SceneFilePath = ""; node.Owner = map; }
      else if (node.Owner is null) node.Owner = map;
      if (!string.IsNullOrEmpty(node.SceneFilePath)) map.SetEditableInstance(node, true);
      if (node is CollisionObject3D collision) collision.CollisionLayer = (collision.CollisionLayer & ~1u) | 2u;
      if (node is GridMap visualGrid) visualGrid.CollisionLayer = (visualGrid.CollisionLayer & ~1u) | 2u;
      if (node is CsgShape3D csg) csg.CollisionLayer = (csg.CollisionLayer & ~1u) | 2u;
      foreach (var child in node.GetChildren()) Prepare(child, localize);
    }
  }

  private static bool HasAnnotations(Node node)
  {
    if (node is BattleAnnotationGrid) return true;
    foreach (var child in node.GetChildren()) if (HasAnnotations(child)) return true;
    return false;
  }

  private static Transform3D WorldTransform(Node3D node) =>
    !node.TopLevel && node.GetParent() is Node3D parent ? WorldTransform(parent) * node.Transform : node.Transform;

  private static bool IsUpright(Basis basis)
  {
    for (int turn = 0; turn < 4; turn++)
      if (basis.IsEqualApprox(new Basis(Vector3.Up, turn * Mathf.Pi / 2))) return true;
    return false;
  }

  public static BattleMapData BuildMap(IEnumerable<(BattleFootprintData Footprint, Transform3D Placement)> pieces, BattleMapData metrics) =>
    BuildMap(pieces, metrics, out _);

  private static BattleMapData BuildMap(IEnumerable<(BattleFootprintData Footprint, Transform3D Placement)> pieces,
    BattleMapData metrics, out SysColGeneric.HashSet<Cell> floors)
  {
    if (!metrics.GridOrigin.IsFinite() || !float.IsFinite(metrics.CellWidth) || metrics.CellWidth <= 0 ||
        !float.IsFinite(metrics.LevelHeight) || metrics.LevelHeight <= 0)
      throw new InvalidOperationException("Grid origin must be finite and cell dimensions positive and finite.");
    var data = new BattleMapData { GridOrigin = metrics.GridOrigin, CellWidth = metrics.CellWidth, LevelHeight = metrics.LevelHeight };
    var grid = new BoardCoordinates(data, Transform3D.Identity);
    var floorCells = new SysColGeneric.HashSet<Cell>();
    floors = floorCells;
    var blocked = new SysColGeneric.HashSet<Cell>();
    var cover = new Dictionary<(Cell Cell, int Side), int>();
    foreach (var (footprint, placement) in pieces)
    {
      if (!IsUpright(placement.Basis) || !placement.Origin.IsFinite())
        throw new InvalidOperationException("Gameplay assets require finite placement, upright quarter-turn rotation and unit scale.");
      foreach (var (local, source) in footprint.Cells)
      {
        var center = placement * (((Vector3)local + Vector3.One / 2) * grid.CellSize);
        var raw = grid.WorldVolumeToTile(center);
        var cell = new Cell(raw.X, raw.Y, raw.Z);
        if (!center.IsEqualApprox(grid.TileToWorldVolumeCenter(raw)))
          throw new InvalidOperationException($"Gameplay cell {local} is not aligned with the map grid.");
        if (source is null || source.CoverAmount < 0 || source.CoverAmount > 100 ||
            (source.CoverDirections & ~(CoverDirections.North | CoverDirections.East | CoverDirections.South | CoverDirections.West)) != 0)
          throw new InvalidOperationException($"Invalid gameplay data at {cell}.");
        var tile = Tile(cell);
        tile.BlocksLineOfSight |= source.BlocksLineOfSight;
        tile.BlocksVerticalLineOfSight |= source.BlocksVerticalLineOfSight;
        if (source.HasFloor) AddFloor(cell, source.SpawnFactionSlot);
        else if (source.SpawnFactionSlot >= 0) throw new InvalidOperationException($"Spawn at {cell} requires an explicit floor.");
        if (source.BlocksMovement && !blocked.Add(cell))
          throw new InvalidOperationException($"Solid gameplay cells overlap at {cell}.");
        if (source.WalkableTop || source.TopBlocksVerticalLineOfSight)
        {
          var top = cell + Cell.Up;
          if (source.WalkableTop) AddFloor(top, -1);
          Tile(top).BlocksVerticalLineOfSight |= source.TopBlocksVerticalLineOfSight;
        }
        for (int side = 0; side < 4; side++)
        {
          if ((source.CoverDirections & Sides[side]) == 0 || source.CoverAmount == 0 || footprint.Cells.ContainsKey(local + Directions[side])) continue;
          var outward = (Cell)(placement.Basis * (Vector3)Directions[side]).Round();
          var neighbor = cell + outward;
          int facing = System.Array.IndexOf(Directions, -outward);
          var key = (neighbor, facing);
          cover[key] = Math.Max(cover.GetValueOrDefault(key), source.CoverAmount);
        }
      }
    }
    foreach (var (cell, tile) in data.Tiles)
    {
      tile.Walkable = floorCells.Contains(cell) && !blocked.Contains(cell);
      if (tile.SpawnFactionSlot >= 0 && !tile.Walkable)
        throw new InvalidOperationException($"Spawn at {cell} is blocked.");
    }
    foreach (var ((cell, side), amount) in cover)
    {
      if (!data.Tiles.TryGetValue(cell, out var tile) || !tile.Walkable) continue;
      if (tile.CoverDirections != CoverDirections.None && tile.CoverAmount != amount)
        throw new InvalidOperationException($"Different cover strengths on separate sides of {cell} require the directional-cover extension.");
      tile.CoverDirections |= Sides[side];
      tile.CoverAmount = amount;
    }
    if (data.Tiles.Count == 0) throw new InvalidOperationException("Map contains no gameplay cells.");
    return data;

    BattleMapTileData Tile(Cell cell)
    {
      if (cell.X < 0 || cell.Y < 0 || cell.Z < 0) throw new InvalidOperationException($"Negative map coordinate {cell}.");
      data.Dimensions = new(Math.Max(data.Dimensions.X, cell.X + 1), Math.Max(data.Dimensions.Y, cell.Y + 1), Math.Max(data.Dimensions.Z, cell.Z + 1));
      if (!data.Tiles.TryGetValue(cell, out var tile)) data.Tiles[cell] = tile = new() { Walkable = false };
      return tile;
    }
    void AddFloor(Cell cell, int spawn)
    {
      if (!floorCells.Add(cell)) throw new InvalidOperationException($"Duplicate floor at {cell}.");
      Tile(cell).SpawnFactionSlot = spawn;
    }
  }
}
