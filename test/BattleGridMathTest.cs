using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleGridMathTest
{
  [TestCase(TestName = "CellToLocalCenter returns the volumetric cell center")]
  public void CellToLocalCenterReturnsTheVolumetricCellCenter()
  {
    Vector3 center = BattleGridMath.CellToLocalCenter(new Vector3I(2, 1, 3));

    Assert.Equal(new Vector3(2.5f, 1.5f, 3.5f), center);
  }

  [TestCase(TestName = "Ray intersection finds the flat ground plane")]
  public void RayIntersectionFindsTheFlatGroundPlane()
  {
    Option<Vector3> hit = BattleGridMath.TryIntersectRayWithHorizontalPlane(
      new Vector3(2.0f, 5.0f, 3.0f),
      Vector3.Down,
      0.0f);

    Assert.True(hit.IsSome);
    Assert.Equal(new Vector3(2.0f, 0.0f, 3.0f), hit.RequireSome());
  }

  [TestCase(TestName = "Local point converts to flat ground cell")]
  public void LocalPointConvertsToFlatGroundCell()
  {
    Option<Vector3I> cell = BattleGridMath.TryLocalPointToFlatGroundCell(
      new Vector3(3.2f, 0.0f, 1.8f),
      new Vector3I(8, 1, 8),
      1.0f);

    Assert.True(cell.IsSome);
    Assert.Equal(new Vector3I(3, 0, 1), cell.RequireSome());
  }

  [TestCase(TestName = "Out of bounds local point returns null cell")]
  public void OutOfBoundsLocalPointReturnsNullCell()
  {
    Option<Vector3I> cell = BattleGridMath.TryLocalPointToFlatGroundCell(
      new Vector3(-0.1f, 0.0f, 1.8f),
      new Vector3I(8, 1, 8),
      1.0f);

    Assert.True(cell.IsNone);
  }
}
