namespace FunProject.Battle;

public readonly record struct TileCover(int North, int East, int South, int West)
{
  public static readonly TileCover None = default;
}
