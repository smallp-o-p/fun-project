#nullable enable
using Godot;
using System;

namespace FunProject.Battle;

public static class BattleGridMath
{
  private const float RayParallelEpsilon = 0.0001f;

  public static Vector3 CellToLocalCenter(Vector3I coordinates, float tileSize = 1.0f)
  {
    if (tileSize <= 0.0f)
      throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive.");

    return new Vector3(
      (coordinates.X + 0.5f) * tileSize,
      (coordinates.Y + 0.5f) * tileSize,
      (coordinates.Z + 0.5f) * tileSize);
  }

  public static Vector3 FlatGroundCellToLocalSurfaceCenter(Vector3I coordinates, float tileSize = 1.0f)
  {
    if (tileSize <= 0.0f)
      throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive.");

    return new Vector3(
      (coordinates.X + 0.5f) * tileSize,
      0.0f,
      (coordinates.Z + 0.5f) * tileSize);
  }

  public static Vector3? TryIntersectRayWithHorizontalPlane(Vector3 rayOrigin, Vector3 rayDirection, float planeY = 0.0f)
  {
    if (Mathf.Abs(rayDirection.Y) <= RayParallelEpsilon)
      return null;

    float distanceAlongRay = (planeY - rayOrigin.Y) / rayDirection.Y;
    if (distanceAlongRay < 0.0f)
      return null;

    return rayOrigin + (rayDirection * distanceAlongRay);
  }

  public static Vector3I? TryLocalPointToFlatGroundCell(Vector3 localPoint, Vector3I dimensions, float tileSize = 1.0f)
  {
    if (!BattleMapData.HasValidDimensions(dimensions))
      throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be positive.");
    if (tileSize <= 0.0f)
      throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive.");

    int x = Mathf.FloorToInt(localPoint.X / tileSize);
    int z = Mathf.FloorToInt(localPoint.Z / tileSize);

    if (x < 0 || z < 0 || x >= dimensions.X || z >= dimensions.Z)
      return null;

    return new Vector3I(x, 0, z);
  }
}
