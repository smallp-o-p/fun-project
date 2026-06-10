namespace FunProject.Battle;

public readonly record struct TileCover(CoverDirections Directions, int Amount)
{
  public static readonly TileCover None = new(CoverDirections.None, 0);
}
