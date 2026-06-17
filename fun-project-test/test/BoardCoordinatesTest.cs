using GdUnit4;
using Godot;
using FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class BoardCoordinatesTest
{
  [TestCase(TestName = "TileToWorldCenter returns the tile's center")]
  public void TileToWorldCenter()
  {
    Assert.Equal(new Vector3(1.5f, 0f, 2.5f), BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 2)));
  }

  [TestCase(TestName = "WorldToTile floors X/Z and rounds Y")]
  public void WorldToTile()
  {
    Assert.Equal(new Vector3I(1, 0, 2), BoardCoordinates.WorldToTile(new Vector3(1.9f, 0.1f, 2.1f)));
  }

  [TestCase(TestName = "Tile -> world center -> tile round-trips")]
  public void RoundTrip()
  {
    var tile = new Vector3I(3, 0, 5);
    Assert.Equal(tile, BoardCoordinates.WorldToTile(BoardCoordinates.TileToWorldCenter(tile)));
  }
}
