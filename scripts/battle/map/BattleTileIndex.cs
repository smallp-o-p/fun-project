using System;

namespace FunProject.Battle;

internal static class BattleTileIndex
{
  internal const byte Walkable = 1;
  internal const byte BlocksSight = 2;
  internal const byte BlocksVerticalSight = 4;

  internal static int Count(int width, int height, int depth)
  {
    if (width <= 0 || height <= 0 || depth <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must be positive.");
    return checked(width * depth * height);
  }

  // Callers prove bounds before flattening; X is contiguous, then Z, then Y.
  internal static int Of(int x, int y, int z, int width, int depth) => x + width * (z + depth * y);
}
