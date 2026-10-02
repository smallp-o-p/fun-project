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
    TileCover northCover = new(40, 0, 0, 0);

    Assert.True(CoverRules.Applies(northCover, CoverDirections.North));
    Assert.True(CoverRules.Applies(northCover, CoverDirections.North | CoverDirections.East));
    Assert.False(CoverRules.Applies(northCover, CoverDirections.East));
    Assert.False(CoverRules.Applies(northCover, CoverDirections.None));
    Assert.False(CoverRules.Applies(new TileCover(0, 0, 0, 0), CoverDirections.North));
    Assert.False(CoverRules.Applies(TileCover.None, CoverDirections.North));
  }

  [TestCase(CoverDirections.None, 0)]
  [TestCase(CoverDirections.North, 10)]
  [TestCase(CoverDirections.East, 30)]
  [TestCase(CoverDirections.South, 50)]
  [TestCase(CoverDirections.West, 70)]
  [TestCase(CoverDirections.North | CoverDirections.East, 30)]
  [TestCase(CoverDirections.North | CoverDirections.West, 70)]
  [TestCase(CoverDirections.South | CoverDirections.East, 50)]
  [TestCase(CoverDirections.South | CoverDirections.West, 70)]
  public void GetAmountUsesStrongestMatchingSide(CoverDirections approach, int expected)
  {
    TileCover cover = new(10, 30, 50, 70);

    Assert.Equal(expected, CoverRules.GetAmount(cover, approach));
  }

  [TestCase]
  public void DiagonalCoverDoesNotAddMatchingSides()
  {
    TileCover cover = new(40, 40, 0, 0);

    Assert.Equal(40, CoverRules.GetAmount(cover, CoverDirections.North | CoverDirections.East));
    Assert.Equal(0, CoverRules.GetAmount(cover, CoverDirections.South | CoverDirections.West));
    Assert.Equal(0, CoverRules.GetAmount(TileCover.None, CoverDirections.North));
  }
}
