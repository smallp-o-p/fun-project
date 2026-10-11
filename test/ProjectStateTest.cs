using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.GameState;
using FunProject.Strategic;
using FunProject.Engineering;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ProjectStateTest
{
  [TestCase]
  public static void ManufacturingCompletionClearsTheJobWithoutChangingStockItself()
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item);
    var state = new CampaignGameState(MakeStart(manufacturingRecipes: [itemRecipe]));
    var session = new GeoscapeSession(state);
    var job = session.StartManufacturing(itemRecipe).RequireRight();

    Assert.True(state.Engineering.CompleteIfDue(1439).IsNone);
    Assert.Equal(Some(job), state.Engineering.ActiveJob);
    Assert.Equal(new ManufacturingCompleted(job), state.Engineering.CompleteIfDue(1440).RequireSome());
    Assert.True(state.Engineering.ActiveJob.IsNone);
    Assert.False(state.Armory.HasAvailableItem(item));
    Assert.True(state.Engineering.CompleteIfDue(1440).IsNone);
  }
}
