using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class CoverRulesTest
{
  [TestCase(TestName = "GetApproach maps cardinal deltas to compass flags")]
  public void GetApproachMapsCardinalDeltasToCompassFlags()
  {
    Vector3I defender = new(4, 0, 4);

    Assert.Equal(CoverDirections.North, CoverRules.GetApproach(new Vector3I(4, 0, 1), defender));
    Assert.Equal(CoverDirections.South, CoverRules.GetApproach(new Vector3I(4, 0, 7), defender));
    Assert.Equal(CoverDirections.East, CoverRules.GetApproach(new Vector3I(7, 0, 4), defender));
    Assert.Equal(CoverDirections.West, CoverRules.GetApproach(new Vector3I(1, 0, 4), defender));
  }

  [TestCase(TestName = "GetApproach combines components for diagonal attackers")]
  public void GetApproachCombinesComponentsForDiagonalAttackers()
  {
    Vector3I defender = new(4, 0, 4);

    Assert.Equal(CoverDirections.North | CoverDirections.East, CoverRules.GetApproach(new Vector3I(6, 0, 1), defender));
    Assert.Equal(CoverDirections.South | CoverDirections.West, CoverRules.GetApproach(new Vector3I(2, 0, 6), defender));
  }

  [TestCase(TestName = "GetApproach returns none for the same tile")]
  public void GetApproachReturnsNoneForTheSameTile()
  {
    Vector3I defender = new(4, 0, 4);

    Assert.Equal(CoverDirections.None, CoverRules.GetApproach(defender, defender));
  }

  [TestCase(TestName = "GetApproach ignores vertical difference")]
  public void GetApproachIgnoresVerticalDifference()
  {
    Assert.Equal(CoverDirections.North, CoverRules.GetApproach(new Vector3I(4, 3, 1), new Vector3I(4, 0, 4)));
    Assert.Equal(CoverDirections.None, CoverRules.GetApproach(new Vector3I(4, 3, 4), new Vector3I(4, 0, 4)));
  }

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
