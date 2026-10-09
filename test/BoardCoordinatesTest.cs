using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class BoardCoordinatesTest
{
  [TestCase(TestName = "TileToWorldCenter returns the tile's center")]
  public static void TileToWorldCenter()
  {
    Assert.Equal(new Vector3(1.5f, 0f, 2.5f), BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 2)));
  }

  [TestCase(TestName = "WorldToTile floors X/Z and rounds Y")]
  public static void WorldToTile()
  {
    Assert.Equal(new Vector3I(1, 0, 2), BoardCoordinates.WorldToTile(new Vector3(1.9f, 0.1f, 2.1f)));
  }

  [TestCase(TestName = "Tile -> world center -> tile round-trips")]
  public static void RoundTrip()
  {
    var tile = new Vector3I(3, 0, 5);
    Assert.Equal(tile, BoardCoordinates.WorldToTile(BoardCoordinates.TileToWorldCenter(tile)));
  }
}
