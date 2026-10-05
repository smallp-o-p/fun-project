using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class BattleMapAuthoring : BattleMap
{
  private static readonly Godot.Vector3I[] Directions = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
  private static readonly CoverDirections[] Sides = [CoverDirections.North, CoverDirections.East, CoverDirections.South, CoverDirections.West];
  private MeshLibrary _palette = new();
  [Export]
  public MeshLibrary Palette
  {
    get => _palette;
    set
    {
      _palette = value;
      if (GetNodeOrNull<GridMap>("Floors") is { } floors) floors.MeshLibrary = value;
      if (GetNodeOrNull<GridMap>("Props") is { } props) props.MeshLibrary = value;
    }
  }
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, int> SpawnSlots { get; set; } = [];
  public GridMap Floors => GetNode<GridMap>("Floors");
  public GridMap Props => GetNode<GridMap>("Props");

  [Export(PropertyHint.File, "*.tscn,*.scn")]
  public string ExportPath { get; set; } = "res://resources/maps/map.tscn";

  [ExportToolButton("Bake BattleMap")] private Callable BakeButton => Callable.From(Bake);
  [ExportToolButton("Export BattleMap")] private Callable ExportButton => Callable.From(Export);

  [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
  public BattleMapAuthoring() => MapData = new();

  public override void _Ready()
  {
    if (Engine.IsEditorHint()) Floors.MeshLibrary = Props.MeshLibrary = Palette;
  }

  public void Bake()
  {
    var data = BuildData(BattleTilePalette.ReadFootprints(Palette));
    Floors.MeshLibrary = Props.MeshLibrary = Palette;
    MapData = data;
    MarkUnsaved();
  }

  public void Export()
  {
    Bake();
    var exported = new BattleMap { Name = Name, MapData = MapData };
    try
    {
      exported.FloorTiles = (GridMap)Floors.Duplicate();
      exported.PropTiles = (GridMap)Props.Duplicate();
      foreach (var grid in new[] { exported.FloorTiles, exported.PropTiles })
      {
        exported.AddChild(grid);
        grid.Owner = exported;
      }
      using var scene = new PackedScene();
      var error = scene.Pack(exported);
      if (error == Error.Ok) error = ResourceSaver.Save(scene, ExportPath);
      if (error != Error.Ok) throw new InvalidOperationException($"Could not export map to {ExportPath}: {error}.");
#if TOOLS
      if (Engine.IsEditorHint() && IsInsideTree()) EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(ExportPath);
#endif
    }
    finally { exported.Free(); }
  }

  private void MarkUnsaved()
  {
#if TOOLS
    if (Engine.IsEditorHint() && IsInsideTree()) EditorInterface.Singleton.MarkSceneAsUnsaved();
#endif
  }

  private void ValidateGrids()
  {
    if (!Transform.IsEqualApprox(Transform3D.Identity) || !WorldTransform(this).IsEqualApprox(Transform3D.Identity))
      throw new InvalidOperationException("Map root must use the fixed board origin, rotation and unit scale.");
    foreach (var grid in new[] { Floors, Props })
      if (grid.GetScript().VariantType != Variant.Type.Nil || !grid.Transform.IsEqualApprox(Transform3D.Identity) || grid.TopLevel ||
          !grid.CellSize.IsEqualApprox(Vector3.One) || !Mathf.IsEqualApprox(grid.CellScale, 1) || !grid.CellCenterX || !grid.CellCenterY || !grid.CellCenterZ)
        throw new InvalidOperationException($"{grid.Name}: use a plain GridMap with identity transform, centered unit cells and Cell Scale 1.");
  }

  internal BattleMapData BuildData(Godot.Collections.Dictionary<int, Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>> footprints)
  {
    ValidateGrids();
    var pieces = new List<(Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint, Transform3D Placement)>();
    var support = new SysColGeneric.Dictionary<Godot.Vector3I, (Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint, Godot.Vector3I Local)>();
    foreach (var (footprint, placement) in Placements(Floors))
    {
      if (footprint.Count == 0) throw new InvalidOperationException("Floors items must supply ground floor cells.");
      foreach (var (local, source) in footprint)
      {
        var cell = PlacedCell(local, placement);
        if (!source.HasFloor || cell.Y != 0 || !support.TryAdd(cell, (footprint, local)))
          throw new InvalidOperationException($"Floors require unique ground floor claims at {cell}.");
      }
      pieces.Add((footprint, placement));
    }
    var occupied = new SysColGeneric.HashSet<Godot.Vector3I>();
    foreach (var (footprint, placement) in Placements(Props))
    {
      foreach (var local in footprint.Keys)
      {
        var cell = PlacedCell(local, placement);
        if (!support.TryGetValue(new(cell.X, 0, cell.Z), out var floor))
          throw new InvalidOperationException($"Prop volume at {cell} needs painted ground floor support.");
        if (!occupied.Add(cell)) throw new InvalidOperationException($"Prop volume cells overlap at {cell}.");
        if (cell.Y != 0) continue;
        floor.Footprint.Remove(floor.Local);
        footprint[local].HasFloor = true;
      }
      pieces.Add((footprint, placement));
    }
    var data = BuildMap(pieces);
    foreach (var (cell, slot) in SpawnSlots)
    {
      if (slot < 0 || !data.Tiles.TryGetValue(cell, out var tile) || !tile.Walkable)
        throw new InvalidOperationException($"Spawn at {cell} requires a walkable floor and nonnegative faction slot.");
      tile.SpawnFactionSlot = slot;
    }
    return data;

    IEnumerable<(Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint, Transform3D Placement)> Placements(GridMap grid)
    {
      foreach (var anchor in grid.GetUsedCells())
      {
        var basis = grid.GetCellItemBasis(anchor);
        if (anchor.Y != 0 || !IsUpright(basis) || !footprints.TryGetValue(grid.GetCellItem(anchor), out var source))
          throw new InvalidOperationException($"{grid.Name}: {anchor} needs an exported item, ground anchor and upright Y quarter-turn.");
        var placement = new Transform3D(basis, grid.MapToLocal(anchor) - basis * (Vector3.One / 2));
        Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> copy = [];
        foreach (var (local, flags) in source)
        {
          copy[local] = (BattleFootprintData)flags.Duplicate();
        }
        yield return (copy, placement);
      }
    }
  }

  private static Transform3D WorldTransform(Node3D node) =>
    !node.TopLevel && node.GetParent() is Node3D parent ? WorldTransform(parent) * node.Transform : node.Transform;

  private static Godot.Vector3I PlacedCell(Godot.Vector3I local, Transform3D placement)
  {
    var center = placement * ((Vector3)local + Vector3.One / 2);
    var cell = (Godot.Vector3I)center.Floor();
    if (!center.IsFinite() || !center.IsEqualApprox((Vector3)cell + Vector3.One / 2) || cell.X < 0 || cell.Y < 0 || cell.Z < 0)
      throw new InvalidOperationException($"Gameplay cell {local} is not aligned with the nonnegative map grid.");
    return cell;
  }

  internal static bool IsUpright(Basis basis)
  {
    for (int turn = 0; turn < 4; turn++)
      if (basis.IsEqualApprox(new Basis(Vector3.Up, turn * Mathf.Pi / 2))) return true;
    return false;
  }

  public static BattleMapData BuildMap(IEnumerable<(Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint, Transform3D Placement)> pieces)
  {
    var data = new BattleMapData();
    var floorCells = new SysColGeneric.HashSet<Godot.Vector3I>();
    var blocked = new SysColGeneric.HashSet<Godot.Vector3I>();
    var cover = new Dictionary<(Godot.Vector3I Cell, int Side), int>();
    foreach (var (footprint, placement) in pieces)
    {
      if (!IsUpright(placement.Basis) || !placement.Origin.IsFinite())
        throw new InvalidOperationException("Gameplay assets require finite placement, upright quarter-turn rotation and unit scale.");
      foreach (var (local, source) in footprint)
      {
        var cell = PlacedCell(local, placement);
        if (source.WalkableTop || (source.HasFloor && cell.Y != 0))
          throw new InvalidOperationException($"Only one walkable ground layer is supported at {cell}.");
        var tile = Tile(cell);
        tile.BlocksLineOfSight |= source.BlocksLineOfSight;
        tile.BlocksVerticalLineOfSight |= source.BlocksVerticalLineOfSight;
        if (source.HasFloor) AddFloor(cell);
        if (source.BlocksMovement && !blocked.Add(cell))
          throw new InvalidOperationException($"Solid gameplay cells overlap at {cell}.");
        if (source.TopBlocksVerticalLineOfSight) Tile(cell + Godot.Vector3I.Up).BlocksVerticalLineOfSight = true;
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
