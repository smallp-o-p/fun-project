using System;
using FunProject.Progression;
using GdUnit4;
using static FunProject.Tests.ProgressionTestFactory;

[TestSuite]
[RequireGodotRuntime]
public partial class UnitProgressionCommitTest
{
  [TestCase(TestName = "Committing is free; a unit may hold at most two committed paths")]
  public void CommitRules()
  {
    var progression = new UnitProgression();
    var first = MakePath("Alpha", MakeStep(1));
    var second = MakePath("Beta", MakeStep(1));
    var third = MakePath("Gamma", MakeStep(1));

    Assert.True(progression.TryCommit(first));
    Assert.True(progression.TryCommit(second));
    Assert.Equal(0, progression.CurrencyPoints);
    Assert.Equal(2, progression.CommittedPaths.Count);
    Assert.Equal(first, progression.CommittedPaths[0]);
    Assert.Equal(second, progression.CommittedPaths[1]);

    Assert.False(progression.TryCommit(third));
    Assert.Equal(2, progression.CommittedPaths.Count);
  }

  [TestCase(TestName = "Committing the same path twice is rejected without consuming a slot")]
  public void DuplicateCommitRejected()
  {
    var progression = new UnitProgression();
    var path = MakePath("Alpha", MakeStep(1));

    Assert.True(progression.TryCommit(path));
    Assert.False(progression.TryCommit(path));
    Assert.Equal(1, progression.CommittedPaths.Count);
  }

  [TestCase(TestName = "Committing a path with no steps throws (authoring mistake)")]
  public void EmptyPathThrows()
  {
    var progression = new UnitProgression();
    Assert.Throws<InvalidOperationException>(() => progression.TryCommit(MakePath("Empty")));
  }

  [TestCase(TestName = "AwardPoints accumulates; non-positive amounts are caller bugs")]
  public void AwardPointsRules()
  {
    var progression = new UnitProgression();
    progression.AwardPoints(3);
    progression.AwardPoints(2);
    Assert.Equal(5, progression.CurrencyPoints);

    Assert.Throws<ArgumentOutOfRangeException>(() => progression.AwardPoints(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => progression.AwardPoints(-1));
  }
}
