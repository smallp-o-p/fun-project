using FunProject.GameState;
using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class ProjectStateTest
{
  [TestCase]
  public void ManufacturingCompletionClearsTheJobWithoutChangingStockItself()
  {
    var item = MakeItemData();
    var state = new GameState(MakeStart(manufacturableItems: [item]));
    var session = new GeoscapeSession(state);
    var job = session.StartManufacturing(item).RequireRight();

    Assert.True(state.Engineering.CompleteIfDue(1439).IsNone);
    Assert.Equal(Some(job), state.Engineering.ActiveJob);
    Assert.Equal(new ManufacturingCompleted(job), state.Engineering.CompleteIfDue(1440).RequireSome());
    Assert.True(state.Engineering.ActiveJob.IsNone);
    Assert.False(state.Armory.HasAvailableItem(item));
    Assert.True(state.Engineering.CompleteIfDue(1440).IsNone);
  }
}
