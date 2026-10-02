using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class BoardCoordinatesTest
{
  [TestCase]
  public void ConfiguredGridDistinguishesStandingPlaneFromVolumeCenter()
  {
    var map = new BattleMapData { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 };
    var grid = new BoardCoordinates(map, Transform3D.Identity);
    var cell = new Vector3I(2, 1, 3);
    Assert.Equal(new Vector3(15, 5, 27), grid.TileToWorldCenter(cell));
    Assert.Equal(new Vector3(15, 6.5f, 27), grid.TileToWorldVolumeCenter(cell));
    Assert.Equal(cell, grid.WorldToTile(grid.TileToWorldCenter(cell)));
    Assert.Equal(cell, grid.WorldVolumeToTile(grid.TileToWorldVolumeCenter(cell)));
  }

  [TestCase]
  public void MapTransformAndNegativeCoordinatesRoundTrip()
  {
    var transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), new(4, 7, -5));
    var grid = new BoardCoordinates(new BattleMapData { CellWidth = 2, LevelHeight = 3 }, transform);
    var cell = new Vector3I(-2, 2, 3);
    Assert.Equal(cell, grid.WorldToTile(grid.TileToWorldCenter(cell)));
    Assert.Equal(cell, grid.WorldVolumeToTile(grid.TileToWorldVolumeCenter(cell)));
  }

  [TestCase]
  public void PickingFloorsHorizontalCoordinatesAndRoundsSurfaceHeight()
  {
    Assert.Equal(new Vector3I(1, 0, 2), BoardCoordinates.UnitGrid.WorldToTile(new(1.9f, 0.1f, 2.1f)));
    Assert.Equal(new Vector3(1.5f, 0, 2.5f), BoardCoordinates.UnitGrid.TileToWorldCenter(new(1, 0, 2)));
    Assert.Equal(new Vector3I(-1, 0, 0), BoardCoordinates.UnitGrid.WorldVolumeToTile(new(-0.01f, 0.5f, 0.5f)));
  }
}
