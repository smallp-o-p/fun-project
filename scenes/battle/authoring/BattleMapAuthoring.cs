using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class BattleMapAuthoring : BattleMap
{
  private static readonly Godot.Vector3I[] Directions = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
  private static readonly CoverDirections[] Sides = [CoverDirections.North, CoverDirections.East, CoverDirections.South, CoverDirections.West];
  [Export] public Vector3 GridOrigin { get; set; }
  [Export] public float CellWidth { get; set; } = 1;
  [Export] public float LevelHeight { get; set; } = 1;
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, int> SpawnSlots { get; set; } = [];
  [ExportToolButton("Bake BattleMap")] private Callable BakeButton => Callable.From(Bake);

  [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
  public BattleMapAuthoring() => MapData = new();

  // Bake explicitly, then save this same editable scene. Runtime reads the stored data.
  public void Bake()
  {
    var rootPlacement = WorldTransform(this);
    if (!IsUpright(rootPlacement.Basis) || !rootPlacement.Origin.IsFinite())
      throw new InvalidOperationException("Map root requires finite placement, upright quarter-turn rotation and unit scale.");
    var pieces = new List<(Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>, Transform3D)>();
    var rootInverse = rootPlacement.AffineInverse();
    Collect(this);
    var metrics = new BattleMapData { GridOrigin = GridOrigin, CellWidth = CellWidth, LevelHeight = LevelHeight };
    var data = BuildMap(pieces, metrics, out var floors);
    foreach (var (cell, slot) in SpawnSlots)
    {
      if (slot < 0 || !data.Tiles.TryGetValue(cell, out var tile) || !tile.Walkable)
        throw new InvalidOperationException($"Spawn at {cell} requires a walkable floor and nonnegative faction slot.");
      tile.SpawnFactionSlot = slot;
    }
    MapData = data;
    GetNodeOrNull("FloorPicking")?.Free();
    var picking = new StaticBody3D { Name = "FloorPicking", CollisionLayer = 1, CollisionMask = 0 };
    AddChild(picking);
    picking.Owner = this;
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
      collider.Owner = this;
    }
    PrepareVisualCollision(this);
#if TOOLS
    if (Engine.IsEditorHint()) EditorInterface.Singleton.MarkSceneAsUnsaved();
#endif

    void Collect(Node node)
    {
      BattleAnnotationGrid? annotations = null;
      foreach (var child in node.GetChildren())
        if (child is BattleAnnotationGrid grid)
        {
          if (annotations is not null) throw new InvalidOperationException($"{node.Name}: use one annotation GridMap per asset.");
          annotations = grid;
        }
      if (annotations is not null)
      {
        if (node is not Node3D asset) throw new InvalidOperationException($"{node.Name}: annotation assets require a Node3D root.");
        pieces.Add((annotations.BuildFootprint(new(CellWidth, LevelHeight, CellWidth)), rootInverse * WorldTransform(asset)));
      }
      foreach (var child in node.GetChildren()) Collect(child);
    }
  }

  public override void _Ready()
  {
    if (!Engine.IsEditorHint()) RemoveAnnotations(this);
  }

  private static void RemoveAnnotations(Node node)
  {
    foreach (var child in node.GetChildren())
      if (child is BattleAnnotationGrid) child.Free();
      else RemoveAnnotations(child);
  }

  private void PrepareVisualCollision(Node node)
  {
    foreach (var child in node.GetChildren())
    {
      if (node == this && child.Name == "FloorPicking") continue;
      if (!string.IsNullOrEmpty(child.SceneFilePath)) SetEditableInstance(child, true);
      if (child is CollisionObject3D collision) collision.CollisionLayer = (collision.CollisionLayer & ~1u) | 2u;
      if (child is GridMap visualGrid) visualGrid.CollisionLayer = (visualGrid.CollisionLayer & ~1u) | 2u;
      if (child is CsgShape3D csg) csg.CollisionLayer = (csg.CollisionLayer & ~1u) | 2u;
      PrepareVisualCollision(child);
    }
  }

  private static Transform3D WorldTransform(Node3D node) =>
    !node.TopLevel && node.GetParent() is Node3D parent ? WorldTransform(parent) * node.Transform : node.Transform;

  internal static bool IsUpright(Basis basis)
  {
    for (int turn = 0; turn < 4; turn++)
      if (basis.IsEqualApprox(new Basis(Vector3.Up, turn * Mathf.Pi / 2))) return true;
    return false;
  }

  public static BattleMapData BuildMap(IEnumerable<(Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint, Transform3D Placement)> pieces, BattleMapData metrics) =>
    BuildMap(pieces, metrics, out _);

  private static BattleMapData BuildMap(IEnumerable<(Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint, Transform3D Placement)> pieces,
    BattleMapData metrics, out SysColGeneric.HashSet<Godot.Vector3I> floors)
  {
    if (!metrics.GridOrigin.IsFinite() || !float.IsFinite(metrics.CellWidth) || metrics.CellWidth <= 0 ||
        !float.IsFinite(metrics.LevelHeight) || metrics.LevelHeight <= 0)
      throw new InvalidOperationException("Grid origin must be finite and cell dimensions positive and finite.");
    var data = new BattleMapData { GridOrigin = metrics.GridOrigin, CellWidth = metrics.CellWidth, LevelHeight = metrics.LevelHeight };
    var grid = new BoardCoordinates(data, Transform3D.Identity);
    var floorCells = new SysColGeneric.HashSet<Godot.Vector3I>();
    floors = floorCells;
    var blocked = new SysColGeneric.HashSet<Godot.Vector3I>();
    var cover = new Dictionary<(Godot.Vector3I Cell, int Side), int>();
    foreach (var (footprint, placement) in pieces)
    {
      if (!IsUpright(placement.Basis) || !placement.Origin.IsFinite())
        throw new InvalidOperationException("Gameplay assets require finite placement, upright quarter-turn rotation and unit scale.");
      foreach (var (local, source) in footprint)
      {
        var center = placement * (((Vector3)local + Vector3.One / 2) * grid.CellSize);
        var raw = grid.WorldVolumeToTile(center);
        var cell = new Godot.Vector3I(raw.X, raw.Y, raw.Z);
        if (!center.IsEqualApprox(grid.TileToWorldVolumeCenter(raw)))
          throw new InvalidOperationException($"Gameplay cell {local} is not aligned with the map grid.");
        if (source is null || source.CoverAmount < 0 || source.CoverAmount > 100 ||
            (source.CoverDirections & ~(CoverDirections.North | CoverDirections.East | CoverDirections.South | CoverDirections.West)) != 0)
          throw new InvalidOperationException($"Invalid gameplay data at {cell}.");
        var tile = Tile(cell);
        tile.BlocksLineOfSight |= source.BlocksLineOfSight;
        tile.BlocksVerticalLineOfSight |= source.BlocksVerticalLineOfSight;
        if (source.HasFloor) AddFloor(cell);
        if (source.BlocksMovement && !blocked.Add(cell))
          throw new InvalidOperationException($"Solid gameplay cells overlap at {cell}.");
        if (source.WalkableTop || source.TopBlocksVerticalLineOfSight)
        {
          var top = cell + Godot.Vector3I.Up;
          if (source.WalkableTop) AddFloor(top);
          Tile(top).BlocksVerticalLineOfSight |= source.TopBlocksVerticalLineOfSight;
        }
        for (int side = 0; side < 4; side++)
        {
          if ((source.CoverDirections & Sides[side]) == 0 || source.CoverAmount == 0 || footprint.ContainsKey(local + Directions[side])) continue;
          var outward = (Godot.Vector3I)(placement.Basis * (Vector3)Directions[side]).Round();
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

    BattleMapTileData Tile(Godot.Vector3I cell)
    {
      if (cell.X < 0 || cell.Y < 0 || cell.Z < 0) throw new InvalidOperationException($"Negative map coordinate {cell}.");
      data.Dimensions = new(Math.Max(data.Dimensions.X, cell.X + 1), Math.Max(data.Dimensions.Y, cell.Y + 1), Math.Max(data.Dimensions.Z, cell.Z + 1));
      if (!data.Tiles.TryGetValue(cell, out var tile)) data.Tiles[cell] = tile = new() { Walkable = false };
      return tile;
    }
    void AddFloor(Godot.Vector3I cell)
    {
      if (!floorCells.Add(cell)) throw new InvalidOperationException($"Duplicate floor at {cell}.");
    }
  }
}
