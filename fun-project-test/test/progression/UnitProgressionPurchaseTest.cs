using System;
using FunProject.Progression;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class UnitProgressionPurchaseTest
{
  [TestCase(TestName = "Steps unlock strictly in chain order, deducting the authored cost")]
  public void LinearUnlockOrderAndCost()
  {
    var progression = new UnitProgression();
    var step1 = TestData.MakeStep(1);
    var step2 = TestData.MakeStep(1);
    var path = TestData.MakePath("Alpha", step1, step2);
    progression.TryCommit(path);

    progression.AwardPoints(2);
    var first = progression.TryUnlockNext(path).RequireSome();
    Assert.Equal(step1, first.Step);
    Assert.Equal(path, first.Path);
    Assert.Equal(1, progression.CurrencyPoints);
    Assert.Equal(1, progression.StepsUnlockedFor(path));

    var second = progression.TryUnlockNext(path).RequireSome();
    Assert.Equal(step2, second.Step);
    Assert.Equal(0, progression.CurrencyPoints);
    Assert.Equal(2, progression.StepsUnlockedFor(path));
  }

  [TestCase(TestName = "Unaffordable or completed chains return None and leave state untouched")]
  public void UnaffordableAndCompleteReturnNone()
  {
    var progression = new UnitProgression();
    var path = TestData.MakePath("Alpha", TestData.MakeStep(3));
    progression.TryCommit(path);

    Assert.True(progression.TryUnlockNext(path).IsNone); // 0 < 3: cannot afford
    Assert.Equal(0, progression.CurrencyPoints);
    Assert.Equal(0, progression.StepsUnlockedFor(path));

    progression.AwardPoints(4);
    progression.TryUnlockNext(path).RequireSome();
    Assert.True(progression.TryUnlockNext(path).IsNone); // chain complete
    Assert.Equal(1, progression.CurrencyPoints);
    Assert.Equal(1, progression.StepsUnlockedFor(path));
  }

  [TestCase(TestName = "A step costing less than one point throws (authoring mistake)")]
  public void ZeroCostStepThrows()
  {
    var progression = new UnitProgression();
    var path = TestData.MakePath("Alpha", TestData.MakeStep(0));
    progression.TryCommit(path);

    Assert.Throws<InvalidOperationException>(() => progression.TryUnlockNext(path));
    Assert.Equal(0, progression.StepsUnlockedFor(path));
  }

  [TestCase(TestName = "An uncommitted path returns None and leaves state untouched")]
  public void UncommittedPathReturnsNone()
  {
    var progression = new UnitProgression();
    var path = TestData.MakePath("Alpha", TestData.MakeStep(1));

    Assert.True(progression.TryUnlockNext(path).IsNone);
    Assert.True(progression.NextStep(path).IsNone);
    Assert.Equal(0, progression.CurrencyPoints);
    Assert.Equal(0, progression.StepsUnlockedFor(path));
  }
}
