using System;

namespace FunProject.Battle;

public static class CoverRules
{
  public static CoverDirections GetApproach(Vector3I attackerPosition, Vector3I defenderPosition)
  {
    Vector3I delta = attackerPosition - defenderPosition;
    CoverDirections approach = CoverDirections.None;

    if (delta.Z < 0)
      approach |= CoverDirections.North;
    if (delta.Z > 0)
      approach |= CoverDirections.South;
    if (delta.X > 0)
      approach |= CoverDirections.East;
    if (delta.X < 0)
      approach |= CoverDirections.West;

    return approach;
  }

  // Either-component rule: a diagonal approach uses the strongest matching
  // side. Flanking requires an angle whose components are all uncovered.
  public static int GetAmount(TileCover cover, CoverDirections approach)
  {
    int amount = 0;
    if ((approach & CoverDirections.North) != 0)
      amount = Math.Max(amount, cover.North);
    if ((approach & CoverDirections.East) != 0)
      amount = Math.Max(amount, cover.East);
    if ((approach & CoverDirections.South) != 0)
      amount = Math.Max(amount, cover.South);
    if ((approach & CoverDirections.West) != 0)
      amount = Math.Max(amount, cover.West);

    return amount;
  }
}
