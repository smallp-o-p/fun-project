using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class LanguageExtSetupTest
{
  [TestCase(TestName = "LanguageExt Option is available through global usings")]
  public void LanguageExtOptionIsAvailableThroughGlobalUsings()
  {
    Option<int> actionPoints = Some(4);

    var doubled = actionPoints.Match(
      value => value * 2,
      () => 0);

    Assert.Equal(8, doubled);
  }

  [TestCase(TestName = "RequireSome returns the contained option value")]
  public void RequireSomeReturnsTheContainedOptionValue()
  {
    Option<int> value = Some(7);

    Assert.Equal(7, value.RequireSome());
  }

  [TestCase(TestName = "RequireSome throws for None")]
  public void RequireSomeThrowsForNone()
  {
    Option<int> value = None;

    Assert.Throws<System.InvalidOperationException>(() => value.RequireSome());
  }
}
