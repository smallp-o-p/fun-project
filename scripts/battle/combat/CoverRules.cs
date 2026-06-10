using Godot;

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

  // Either-component rule: a diagonal approach is blocked when ANY of its
  // compass components is covered. Flanking requires an angle whose
  // components are all uncovered.
  public static bool Applies(TileCover cover, CoverDirections approach)
  {
    return cover.Amount > 0 && (cover.Directions & approach) != CoverDirections.None;
  }
}
