using Godot;

// Pure board <-> world mapping for the presentation layer. Single-level slice: board level Y maps
// directly to world Y; tile size is 1 and centers sit at (x+0.5, y, z+0.5), matching the board
// pathgraph's center convention.
public static class BoardCoordinates
{
  public static Vector3 TileToWorldCenter(Vector3I tile) =>
    new(tile.X + 0.5f, tile.Y, tile.Z + 0.5f);

  public static Vector3I WorldToTile(Vector3 world) =>
    new(Mathf.FloorToInt(world.X), Mathf.RoundToInt(world.Y), Mathf.FloorToInt(world.Z));
}
