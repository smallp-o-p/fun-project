#nullable disable warnings
using System;
using FunProject.GameState;
using FunProject.Currencies;
using FunProject.Items;
using FunProject.Research;
using FunProject.Strategic;
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
    var project = MakeResearch(manufacturingUnlocks: [item]);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    var research = campaign.State.Research;
    var definition = research.ResolveResearch(project, campaign.State).RequireRight();
    Assert.Equal(1U, definition.DurationDays);
    var job = new ResearchJob(definition.Project, 7, 1447);
    Assert.Equal(job, research.Start(job).Job);
    Assert.True(research.CompleteIfDue(1446).IsNone);
    var completed = research.CompleteIfDue(1447).RequireSome();
    Assert.Equal(job, completed.Job);
    Assert.Equal(item, completed.ManufacturingUnlocks[0]);
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
    var replacementItem = MakeItemData();
    var project = MakeResearch(days: 2, manufacturingUnlocks: [item], costs: new() { [currency] = 7 });
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
    project.ManufacturingUnlocks[0] = replacementItem;
    var definition = research.ResolveResearch(project, campaign.State).RequireRight();
    Assert.Equal(2U, definition.DurationDays);
    Assert.Equal(7L, research.GetCosts(project).RequireSome()[currency]);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IDictionary<Currency, long>)research.GetCosts(project).RequireSome()).Clear());
    Assert.Equal(item, definition.ManufacturingUnlocks[0]);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IList<ResearchProject>)catalog).Clear());
    var available = research.GetAvailableProjects(campaign.State);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IList<ResearchProject>)available).Clear());
    research.Start(new ResearchJob(project, 0, 2880));
    Assert.True(campaign.Session.GetResearchCosts(MakeResearch()).IsNone);
    var completed = research.CompleteIfDue(2880).RequireSome();
    Assert.Equal(item, completed.ManufacturingUnlocks[0]);
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<EquippableItemData>)completed.ManufacturingUnlocks).Clear());
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
