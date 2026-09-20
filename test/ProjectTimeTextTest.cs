using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class ProjectTimeTextTest
{

  [TestCase(5L, 8L, "0m")]
  [TestCase(5L, 5L, "0m")]
  [TestCase(80L, 5L, "1h 15m")]
  [TestCase(1565L, 0L, "1d 2h 5m")]
  [TestCase(3_000_000_000L, 0L, "2083333d 8h 0m")]
  [TestCase(6_184_752_904_800L, 0L, "4294967295d 0h 0m")]
  public void RemainingClampsCompletionAndFormatsDifference(long completesAtTick, long tick,
    string expected)
  {
    Assert.Equal(expected, ProjectTimeText.Remaining(completesAtTick, tick));
  }

  [TestCase]
  public void RemainingRejectsOverflowInsteadOfDisplayingWrappedTime()
  {
    Assert.Throws<System.OverflowException>(() => ProjectTimeText.Remaining(long.MaxValue, 0));
  }
}
