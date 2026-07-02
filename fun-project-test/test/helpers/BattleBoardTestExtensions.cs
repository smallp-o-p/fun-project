using FunProject.Battle;
using Godot;

namespace FunProject.Tests;

// Sugar for `board.ValidatePoint(...).RequireSome()`, the pervasive test idiom for turning a raw
// coordinate into a ValidatedPoint.
internal static class BattleBoardTestExtensions
{
  public static BattleBoardState.ValidatedPoint At(this BattleBoardState board, int x, int y, int z) =>
    board.ValidatePoint(new Vector3I(x, y, z)).RequireSome();

  public static BattleBoardState.ValidatedPoint At(this BattleBoardState board, Vector3I position) =>
    board.ValidatePoint(position).RequireSome();
}
