using FunProject.Stats;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
// IHasStats.Fold is the static fold kernel (formerly StatFold.Fold).
public class StatFoldTest
{
  [TestCase(TestName = "Empty modifier list returns the base value")]
  public static void EmptyReturnsBase() =>
    Assert.Equal(10f, IHasStats.Fold(10f, []));

  [TestCase(TestName = "Add modifiers sum onto the base")]
  public static void AddsSum() =>
    Assert.Equal(17f, IHasStats.Fold(10f, [StatModifier.Add(5), StatModifier.Add(2)]));

  [TestCase(TestName = "PercentAdd modifiers are additive with each other")]
  public static void PercentAddIsAdditive() =>
    Assert.Equal(14f, IHasStats.Fold(10f, [StatModifier.PercentAdd(0.2f), StatModifier.PercentAdd(0.2f)]));

  [TestCase(TestName = "Multiply modifiers compound")]
  public static void MultiplyCompounds() =>
    Assert.Equal(14.4f, IHasStats.Fold(10f, [StatModifier.Multiply(1.2f), StatModifier.Multiply(1.2f)]));

  [TestCase(TestName = "Add then PercentAdd then Multiply apply in fixed buckets regardless of order")]
  public static void BucketsAreOrderIndependent()
  {
    StatModifier[] ordered = [StatModifier.Add(10), StatModifier.PercentAdd(0.5f), StatModifier.Multiply(2f)];
    StatModifier[] shuffled = [StatModifier.Multiply(2f), StatModifier.Add(10), StatModifier.PercentAdd(0.5f)];
    // (10 + 10) * (1 + 0.5) * 2 = 60
    Assert.Equal(60f, IHasStats.Fold(10f, ordered));
    Assert.Equal(60f, IHasStats.Fold(10f, shuffled));
  }

  [TestCase(TestName = "Override wins and ignores other arithmetic")]
  public static void OverrideWins() =>
    Assert.Equal(3f, IHasStats.Fold(10f, [StatModifier.Add(100), StatModifier.Override(3)]));

  [TestCase(TestName = "CapMax clamps the final value even when an Add follows it")]
  public static void CapMaxIsFinalClamp() =>
    Assert.Equal(75f, IHasStats.Fold(0f, [StatModifier.CapMax(75), StatModifier.Add(100)]));

  [TestCase(TestName = "CapMin clamps the final value even when an Add precedes it")]
  public static void CapMinIsFinalClamp() =>
    Assert.Equal(0f, IHasStats.Fold(5f, [StatModifier.Add(-50), StatModifier.CapMin(0)]));

}
