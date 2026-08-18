using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

// Editor-only GridMap authoring tool. You paint terrain directly on this GridMap; the
// MeshLibrary item at each painted cell is mapped (by item NAME) to a BattleMapTileData gameplay
// brush via the Brushes palette, and Bake writes a BattleMapData .tres. A GridMap cell
// Vector3I is already board space (X=width, Y=elevation/level, Z=depth), so no remapping is
// needed. The pure bake transform lives in the static BuildMap (kept unit-testable); the
// instance Bake() just wires the GridMap read + save to the button.
[Tool]
public partial class BattleMapAuthoring : GridMap
{
  [Export] public Godot.Collections.Dictionary<StringName, BattleMapTileData> Brushes { get; set; } = [];
  [Export] public string TargetPath { get; set; } = "res://resources/maps/untitled_map.tres";
  [Export] public Resource? AssociatedData = null;

  [ExportToolButton("Bake to BattleMapData")]
  public Callable BakeButton => Callable.From(Bake);

  private void Bake()
  {
    var painted = ReadPaintedCells();
    if (painted.Count == 0)
    {
      GD.PushError("BattleMapAuthoring: no painted cells found on the GridMap.");
      return;
    }

    BattleMapData map = BuildMap(painted);
    Error error = ResourceSaver.Save(map, TargetPath);
    if (error != Error.Ok)
    {
      GD.PushError($"BattleMapAuthoring: failed to save '{TargetPath}' ({error}).");
      return;
    }

    // ResourceSaver.Save writes to disk, but the editor keeps its own filesystem/resource
    // cache and won't notice. Tell it the file changed so the FileSystem dock shows the bake
    // and any open/loaded copy reloads from disk — otherwise a stale cached copy can clobber
    // the saved data on the next project save.
    if (Engine.IsEditorHint())
      EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(TargetPath);

    GD.Print($"BattleMapAuthoring: baked {painted.Count} cells -> {TargetPath}");

    AssociatedData = GD.Load<Resource>(TargetPath);
  }

  private List<(Godot.Vector3I Coordinates, BattleMapTileData Brush)> ReadPaintedCells()
  {
    var painted = new List<(Godot.Vector3I Coordinates, BattleMapTileData Brush)>();

    foreach (Godot.Vector3I cell in GetUsedCells())
    {
      int itemId = GetCellItem(cell);
      if (itemId == InvalidCellItem)
        continue;

      if (MeshLibrary is null)
      {
        GD.PrintErr("No meshlib associated!");
        return [];
      }
      StringName itemName = MeshLibrary.GetItemName(itemId);

      if (Brushes.TryGetValue(itemName, out var brush))
      {
        GD.Print($"Painting tile {cell}, {brush}");
        painted.Add((cell, brush));
      }
      else
      {
        GD.PrintErr($"Couldn't find associated brush for {itemName}!");
      }
    }

    return painted;
  }

  // Pure transform: painted cells -> BattleMapData. Normalizes the painted extent to a (0,0,0)
  // origin and keys each cell to its brush (the same brush instance is reused across cells of
  // the same type). Static and free of GridMap/scene state, so it stays unit-testable without a
  // live GridMap.
  public static BattleMapData BuildMap(IReadOnlyList<(Godot.Vector3I Coordinates, BattleMapTileData Brush)> cells)
  {
    if (cells is null || cells.Count == 0)
      throw new InvalidOperationException("Cannot bake a BattleMapData from zero painted cells.");

    int minX = int.MaxValue, minZ = int.MaxValue;
    int maxX = int.MinValue, maxZ = int.MinValue, maxLevel = int.MinValue;
    foreach (var cell in cells)
    {
      Godot.Vector3I c = cell.Coordinates;
      minX = Math.Min(minX, c.X);
      maxX = Math.Max(maxX, c.X);
      minZ = Math.Min(minZ, c.Z);
      maxZ = Math.Max(maxZ, c.Z);
      maxLevel = Math.Max(maxLevel, c.Y);
    }

    var dimensions = new Godot.Vector3I(maxX - minX + 1, maxLevel + 1, maxZ - minZ + 1);
    var tiles = new Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData>();

    foreach (var cell in cells)
    {
      var normalized = new Godot.Vector3I(cell.Coordinates.X - minX, cell.Coordinates.Y, cell.Coordinates.Z - minZ);
      tiles[normalized] = cell.Brush;
    }

    return new BattleMapData
    {
      Dimensions = dimensions,
      Tiles = tiles,
    };
  }
}
