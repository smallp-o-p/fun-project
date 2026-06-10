using System;

namespace FunProject.Battle;

// Board compass convention: North = -Z, South = +Z, East = +X, West = -X
// (board X = width, Z = depth). "North cover" protects against attackers
// positioned to the defender's north.
[Flags]
public enum CoverDirections
{
  None = 0,
  North = 1 << 0,
  South = 1 << 1,
  East = 1 << 2,
  West = 1 << 3,
}
