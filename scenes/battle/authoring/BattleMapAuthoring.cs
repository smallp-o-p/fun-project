using FunProject.Battle;
using Godot;
using System;
using Godot.Collections;

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

  [Export] public string TargetPath { get; set; } = "res://resources/maps/untitled_map.tres";

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
    if (Palette is null)
    {
      GD.PushError("Palette is not set!");
      return;
    }

    if (MeshLibrary is null)
    {
      GD.PushError("Mesh Library is required!");
      return;
    }

    var cells = GetPaintedCells();

    if (cells.Count == 0)
    {
      GD.PushError("BattleMapAuthoring: no painted cells found on the GridMap.");
      return;
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

    var scene = new PackedScene();
    var res = scene.Pack(map);

    if (res != Error.Ok)
      throw new InvalidOperationException("Failed to pack map");

    var saveRes = ResourceSaver.Save(scene, TargetPath);

    if (saveRes != Error.Ok)
      throw new InvalidOperationException($"Failed to save map at {TargetPath}");

    GD.Print($"Successfully saved map to {TargetPath}");
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
        painted[cell] = value;
      else
        throw new InvalidOperationException($"There is no data associated with tile {itemName}");
    }

    return painted;
  }

  private static BattleMapData BuildMap(Dictionary<Godot.Vector3I, BattleMapTileData> cells)
  {
    int minX = int.MaxValue, minZ = int.MaxValue;
    int maxX = int.MinValue, maxZ = int.MinValue, maxLevel = int.MinValue;

    foreach (var (cell, _) in cells)
    {
      minX = Math.Min(minX, cell.X);
      maxX = Math.Max(maxX, cell.X);
      minZ = Math.Min(minZ, cell.Z);
      maxZ = Math.Max(maxZ, cell.Z);
      maxLevel = Math.Max(maxLevel, cell.Y);
    }

    var dimensions = new Godot.Vector3I(maxX - minX + 1, maxLevel + 1, maxZ - minZ + 1);

    return new BattleMapData
    {
      Dimensions = dimensions,
      Tiles = cells,
    };
  }
}
