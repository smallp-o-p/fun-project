using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;

// This suite exercises the Godot-typed bake path (GridMap cells -> authored BattleMapData), so
// every coordinate below is explicitly a Godot.Vector3I (the authored map's coordinate type),
// not the runtime FunProject.Core.Vector3I.
[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapBakerTest
{
  [TestCase(TestName = "Bake normalizes negative coordinates to a zero origin")]
  public void BakeNormalizesNegativeCoordinatesToZeroOrigin()
  {
    var cells = new List<(Godot.Vector3I, BattleMapTileData)>
    {
      (new Godot.Vector3I(-2, 0, -3), TestData.FloorTile()),
      (new Godot.Vector3I(-1, 0, -3), TestData.FloorTile()),
    };

    BattleMapData map = BattleMapAuthoring.BuildMap(cells);

    Assert.Equal(new Godot.Vector3I(2, 1, 1), map.Dimensions);
    Assert.True(map.Tiles.ContainsKey(new Godot.Vector3I(0, 0, 0)));
    Assert.True(map.Tiles.ContainsKey(new Godot.Vector3I(1, 0, 0)));
  }

  [TestCase(TestName = "Bake keys each painted cell to its brush")]
  public void BakeKeysEachPaintedCellToItsBrush()
  {
    var cells = new List<(Godot.Vector3I, BattleMapTileData)>
    {
      (new Godot.Vector3I(0, 0, 0), TestData.FloorTile()),
      (new Godot.Vector3I(1, 0, 0), TestData.WallTile()),
    };

    BattleMapData map = BattleMapAuthoring.BuildMap(cells);
    BattleMapTileData floor = map.Tiles[new Godot.Vector3I(0, 0, 0)];
    BattleMapTileData wall = map.Tiles[new Godot.Vector3I(1, 0, 0)];

    Assert.True(floor.Walkable);
    Assert.False(floor.BlocksLineOfSight);

    Assert.False(wall.Walkable);
    Assert.True(wall.BlocksLineOfSight);
  }

  [TestCase(TestName = "Bake preserves cover on the tile")]
  public void BakePreservesCoverOnTheTile()
  {
    var coverBrush = new BattleMapTileData
    {
      Walkable = true,
      CoverDirections = CoverDirections.North | CoverDirections.East,
      CoverAmount = 40,
    };
    var cells = new List<(Godot.Vector3I, BattleMapTileData)> { (new Godot.Vector3I(0, 0, 0), coverBrush) };

    BattleMapData map = BattleMapAuthoring.BuildMap(cells);
    BattleMapTileData tile = map.Tiles[new Godot.Vector3I(0, 0, 0)];

    Assert.Equal(CoverDirections.North | CoverDirections.East, tile.CoverDirections);
    Assert.Equal(40, tile.CoverAmount);
  }

  [TestCase(TestName = "Bake sets multi-level dimensions")]
  public void BakeSetsMultiLevelDimensions()
  {
    var cells = new List<(Godot.Vector3I, BattleMapTileData)>
    {
      (new Godot.Vector3I(0, 0, 0), TestData.FloorTile()),
      (new Godot.Vector3I(0, 1, 0), TestData.FloorTile()),
    };

    BattleMapData map = BattleMapAuthoring.BuildMap(cells);

    Assert.Equal(new Godot.Vector3I(1, 2, 1), map.Dimensions);
    Assert.Equal(2, map.Tiles.Count);
  }

  [TestCase(TestName = "Bake throws on empty input")]
  public void BakeThrowsOnEmptyInput()
  {
    Assert.Throws<System.InvalidOperationException>(
      () => BattleMapAuthoring.BuildMap(new List<(Godot.Vector3I, BattleMapTileData)>()));
  }
}
