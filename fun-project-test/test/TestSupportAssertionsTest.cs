using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class TestSupportAssertionsTest
{
  [TestCase]
  public void EitherExtractionPreservesItsExpectedSide()
  {
    Assert.Equal(4, Right<string, int>(4).RequireRight());
    Assert.Equal("failure", Left<string, int>("failure").RequireLeft());
    Assert.Throws<InvalidOperationException>(() => Left<string, int>("failure").RequireRight());
    Assert.Throws<InvalidOperationException>(() => Right<string, int>(4).RequireLeft());
  }

  [TestCase]
  public void EventChecksPreserveOrderAndRequireExactlyOneMatch()
  {
    object[] events = ["cause", 3, 4];
    Assert.Equal(2, events.EventsOf<int>().Length);
    Assert.Equal(2, events.EventIndex<int>(value => value == 4));
    events.EventBefore<string, int>();
    Assert.Equal("cause", events.SingleEvent<string>());
    Assert.Throws<InvalidOperationException>(() => events.SingleEvent<int>());
    Assert.Throws<InvalidOperationException>(() => events.SingleEvent<double>());
  }
}
