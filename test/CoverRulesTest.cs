using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class CoverRulesTest
{
  [TestCase(4, 0, 1, CoverDirections.North, TestName = "North approach")]
  [TestCase(4, 0, 7, CoverDirections.South, TestName = "South approach")]
  [TestCase(7, 0, 4, CoverDirections.East, TestName = "East approach")]
  [TestCase(1, 0, 4, CoverDirections.West, TestName = "West approach")]
  [TestCase(6, 0, 1, CoverDirections.North | CoverDirections.East, TestName = "North-east approach")]
  [TestCase(2, 0, 6, CoverDirections.South | CoverDirections.West, TestName = "South-west approach")]
  [TestCase(4, 0, 4, CoverDirections.None, TestName = "Same tile yields no approach")]
  [TestCase(4, 3, 1, CoverDirections.North, TestName = "Vertical difference ignored, north approach")]
  [TestCase(4, 3, 4, CoverDirections.None, TestName = "Vertical difference ignored, same column")]
  public void GetApproach(int x, int y, int z, CoverDirections expected) =>
    Assert.Equal(expected, CoverRules.GetApproach(new(x, y, z), new(4, 0, 4)));

  [TestCase(TestName = "Applies requires an overlapping direction and a positive amount")]
  public void AppliesRequiresOverlappingDirectionAndPositiveAmount()
  {
    TileCover northCover = new(CoverDirections.North, 40);

    Assert.True(CoverRules.Applies(northCover, CoverDirections.North));
    Assert.True(CoverRules.Applies(northCover, CoverDirections.North | CoverDirections.East));
    Assert.False(CoverRules.Applies(northCover, CoverDirections.East));
    Assert.False(CoverRules.Applies(northCover, CoverDirections.None));
    Assert.False(CoverRules.Applies(new TileCover(CoverDirections.North, 0), CoverDirections.North));
    Assert.False(CoverRules.Applies(TileCover.None, CoverDirections.North));
  }
}
