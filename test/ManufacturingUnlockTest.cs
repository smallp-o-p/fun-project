#nullable disable warnings
using System;
using FunProject.Engineering;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ManufacturingUnlockTest
{
  [TestCase]
  public static void UnlockRegistersUnknownRecipesOnceInFirstRegistrationOrderWithoutSideEffects()
  {
    var initial = MakeItemData("Initial");
    var initialRecipe = MakeManufacturingRecipe(initial);
    var second = MakeItemData("Second");
    var secondRecipe = MakeManufacturingRecipe(second);
    var first = MakeItemData("First");
    var firstRecipe = MakeManufacturingRecipe(first);
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturingRecipes: [initialRecipe, initialRecipe],
      researchProjects: [MakeResearch(manufacturingUnlocks: [MakeManufacturingRecipe(MakeItemData("Catalog reward"))])]));
    var engineering = campaign.State.Engineering;

    engineering.Unlock([secondRecipe, firstRecipe, secondRecipe]);
    var options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(3, options.Count);
    Assert.Equal(initial, options[0].Project.Item);
    Assert.Equal(second, options[1].Project.Item);
    Assert.Equal(first, options[2].Project.Item);
    var initialProject = options[0].Project;
    var secondProject = options[1].Project;
    var firstProject = options[2].Project;

    engineering.Unlock([firstRecipe, initialRecipe, secondRecipe]);
    options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(3, options.Count);
    Assert.Equal(initialProject, options[0].Project);
    Assert.Equal(secondProject, options[1].Project);
    Assert.Equal(firstProject, options[2].Project);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(0, campaign.Events.Count);
  }

}
