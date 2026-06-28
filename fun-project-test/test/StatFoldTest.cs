using FunProject.Stats;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
// HasStats.Fold is the static fold kernel (formerly StatFold.Fold).
public class StatFoldTest
{
  [TestCase(TestName = "Empty modifier list returns the base value")]
  public void EmptyReturnsBase() =>
    Assert.Equal(10f, HasStats.Fold(10f, []));

  [TestCase(TestName = "Add modifiers sum onto the base")]
  public void AddsSum() =>
    Assert.Equal(17f, HasStats.Fold(10f, [StatModifier.Add(5), StatModifier.Add(2)]));

  [TestCase(TestName = "PercentAdd modifiers are additive with each other")]
  public void PercentAddIsAdditive() =>
    Assert.Equal(14f, HasStats.Fold(10f, [StatModifier.PercentAdd(0.2f), StatModifier.PercentAdd(0.2f)]));

  [TestCase(TestName = "Multiply modifiers compound")]
  public void MultiplyCompounds() =>
    Assert.Equal(14.4f, HasStats.Fold(10f, [StatModifier.Multiply(1.2f), StatModifier.Multiply(1.2f)]));

  [TestCase(TestName = "Add then PercentAdd then Multiply apply in fixed buckets regardless of order")]
  public void BucketsAreOrderIndependent()
  {
    StatModifier[] ordered = [StatModifier.Add(10), StatModifier.PercentAdd(0.5f), StatModifier.Multiply(2f)];
    StatModifier[] shuffled = [StatModifier.Multiply(2f), StatModifier.Add(10), StatModifier.PercentAdd(0.5f)];
    // (10 + 10) * (1 + 0.5) * 2 = 60
    Assert.Equal(60f, HasStats.Fold(10f, ordered));
    Assert.Equal(60f, HasStats.Fold(10f, shuffled));
  }

  [TestCase(TestName = "Override wins and ignores other arithmetic")]
  public void OverrideWins() =>
    Assert.Equal(3f, HasStats.Fold(10f, [StatModifier.Add(100), StatModifier.Override(3)]));

  [TestCase(TestName = "CapMax clamps the final value even when an Add follows it")]
  public void CapMaxIsFinalClamp() =>
    Assert.Equal(75f, HasStats.Fold(0f, [StatModifier.CapMax(75), StatModifier.Add(100)]));

  [TestCase(TestName = "CapMin clamps the final value even when an Add precedes it")]
  public void CapMinIsFinalClamp() =>
    Assert.Equal(0f, HasStats.Fold(5f, [StatModifier.Add(-50), StatModifier.CapMin(0)]));
}
