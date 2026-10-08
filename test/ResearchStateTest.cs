#nullable disable warnings
using System;
using FunProject.GameState;
using FunProject.Currencies;
using FunProject.Items;
using FunProject.Research;
using FunProject.Strategic;
using FunProject.Engineering;
using GdUnit4;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ResearchStateTest
{
  [TestCase]
  public void CompletionMarksProjectWithoutApplyingManufacturingEffects()
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item);
    var project = MakeResearch(manufacturingUnlocks: [itemRecipe]);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    var research = campaign.State.Research;
    var definition = research.ResolveResearch(project, campaign.State).RequireRight();
    Assert.Equal(1U, definition.DurationDays);
    var job = new ResearchJob(definition.Project, 7, 1447);
    Assert.Equal(job, research.Start(job).Job);
    Assert.True(research.CompleteIfDue(1446).IsNone);
    var completed = research.CompleteIfDue(1447).RequireSome();
    Assert.Equal(job, completed.Job);
    Assert.Equal(itemRecipe, completed.ManufacturingUnlocks[0]);
    Assert.True(research.ActiveJob.IsNone);
    Assert.True(research.IsCompleted(project));
    Assert.True(research.CompleteIfDue(1448).IsNone);
    Assert.Equal(0, campaign.Session.GetManufacturingOptions().Count);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void CatalogAndCapturedOutputsPreserveIdentityAndImmutableMembership()
  {
    var currency = MakeCurrency();
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item);
    var replacementItem = MakeItemData();
    var replacementItemRecipe = MakeManufacturingRecipe(replacementItem);
    var project = MakeResearch(days: 2, manufacturingUnlocks: [itemRecipe], costs: new() { [currency] = 7 });
    var sameName = MakeResearch();
    var start = MakeStart(researchProjects: [project, project, sameName]);
    using var campaign = new GeoscapeFixture(start);
    var research = campaign.State.Research;
    var catalog = research.Projects;
    Assert.Equal(2, catalog.Count);
    Assert.Equal(project, catalog[0]);
    Assert.Equal(sameName, catalog[1]);
    start.ResearchProjects[0] = MakeResearch("Replacement");
    project.DurationDays = 20;
    project.Costs[currency] = 99;
    project.Condition = new NotResearchCondition { Child = new AlwaysResearchCondition() };
    project.ManufacturingUnlocks[0] = replacementItemRecipe;
    var definition = research.ResolveResearch(project, campaign.State).RequireRight();
    Assert.Equal(2U, definition.DurationDays);
    Assert.Equal(7L, research.GetCosts(project)[currency]);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IDictionary<Currency, long>)research.GetCosts(project)).Clear());
    Assert.Equal(itemRecipe, definition.ManufacturingUnlocks[0]);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IList<ResearchProject>)catalog).Clear());
    var available = research.GetAvailableProjects(campaign.State);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IList<ResearchProject>)available).Clear());
    research.Start(new ResearchJob(project, 0, 2880));
    var completed = research.CompleteIfDue(2880).RequireSome();
    Assert.Equal(itemRecipe, completed.ManufacturingUnlocks[0]);
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ManufacturingRecipe>)completed.ManufacturingUnlocks).Clear());
    Assert.True(research.IsCompleted(project));
    Assert.False(research.IsCompleted(sameName));
  }

  [TestCase]
  public void EmptyCatalogWorksAndMalformedCatalogsFailConstruction()
  {
    using var empty = new GeoscapeFixture();
    Assert.Equal(0, empty.State.Research.Projects.Count);
    Assert.False(empty.State.Research.IsCompleted(MakeResearch()));
    Assert.Equal(0, empty.State.Research.GetAvailableProjects(empty.State).Count);
    Assert.True(empty.State.Research.ActiveJob.IsNone);
    var nullArray = MakeStart();
    nullArray.ResearchProjects = null;
    var noCondition = MakeResearch();
    noCondition.Condition = null;
    var noOutputs = MakeResearch();
    noOutputs.ManufacturingUnlocks = null;
    var noCosts = MakeResearch();
    noCosts.Costs = null;
    CampaignStartData[] invalid = [nullArray,
      MakeStart(researchProjects: [null]),
      MakeStart(researchProjects: [noCondition]),
      MakeStart(researchProjects: [noOutputs]),
      MakeStart(researchProjects: [noCosts]),
      MakeStart(researchProjects: [MakeResearch(manufacturingUnlocks: [null])]),
    ];
    foreach (var start in invalid)
      Assert.Throws<ArgumentNullException>(() => new CampaignGameState(start));
  }
}
