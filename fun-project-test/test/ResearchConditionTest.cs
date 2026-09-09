using System;
using FunProject.Research;
using GdUnit4;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ResearchConditionTest
{
  [TestCase]
  public void LogicalConditionsHaveExplicitEmptyAndShortCircuitSemantics()
  {
    using var campaign = new GeoscapeFixture();
    var yes = new AlwaysResearchCondition();
    var no = new NotResearchCondition { Child = yes };
    var exploding = new ResearchTestCondition { Predicate = _ => throw new InvalidOperationException() };
    Assert.True(yes.IsMet(campaign.State));
    Assert.False(no.IsMet(campaign.State));
    Assert.True(new AllResearchConditions().IsMet(campaign.State));
    Assert.False(new AnyResearchConditions().IsMet(campaign.State));
    Assert.True(new AllResearchConditions { Children = [yes, yes] }.IsMet(campaign.State));
    Assert.False(new AnyResearchConditions { Children = [no, no] }.IsMet(campaign.State));
    Assert.False(new AllResearchConditions { Children = [no, exploding] }.IsMet(campaign.State));
    Assert.True(new AnyResearchConditions { Children = [yes, exploding] }.IsMet(campaign.State));
    Assert.False(new AllResearchConditions { Children = [yes, no] }.IsMet(campaign.State));
    Assert.True(new AnyResearchConditions { Children = [no, yes] }.IsMet(campaign.State));
  }

  [TestCase]
  public void AuthoringRejectsNullsAndCyclesButAllowsSharedChildren()
  {
    var self = new AllResearchConditions();
    self.Children = [self];
    var left = new NotResearchCondition { Child = new AlwaysResearchCondition() };
    var right = new AnyResearchConditions { Children = [left] };
    left.Child = right;
    Assert.Throws<InvalidOperationException>(() => new CampaignGameState(MakeStart(
      researchProjects: [MakeResearch(condition: self)])));
    Assert.Throws<InvalidOperationException>(() => new CampaignGameState(MakeStart(
      researchProjects: [MakeResearch(condition: left)])));
    ResearchCondition[] invalid = [
      new AllResearchConditions { Children = null },
      new AnyResearchConditions { Children = [new AlwaysResearchCondition(), null] },
      new NotResearchCondition { Child = null },
      new ResearchCompletedCondition { Project = null },
    ];
    foreach (var condition in invalid)
      Assert.Throws<ArgumentNullException>(() => new CampaignGameState(MakeStart(
        researchProjects: [MakeResearch(condition: condition)])));
    var shared = new AlwaysResearchCondition();
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [MakeResearch(
      condition: new AllResearchConditions { Children = [shared, shared] })]));
    Assert.Equal(1, campaign.State.Research.GetAvailableProjects(campaign.State).Count);
  }

  [TestCase]
  public void CompletionPrerequisiteReadsTheSuppliedCampaignAndDoesNotRecurse()
  {
    var first = MakeResearch("First");
    var second = MakeResearch("Second", condition: new ResearchCompletedCondition { Project = first });
    first.Condition = new ResearchCompletedCondition { Project = second };
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [first, second]));
    using var other = new GeoscapeFixture(MakeStart(researchProjects: [first, second]));
    Assert.Equal(0, campaign.State.Research.GetAvailableProjects(campaign.State).Count);
    // Direct lifecycle test: install a trusted historical job to exercise completion lookup.
    campaign.State.Research.Start(new ResearchJob(first, 0, 1));
    campaign.State.Research.CompleteIfDue(1).RequireSome();
    Assert.True(second.Condition.IsMet(campaign.State));
    Assert.False(second.Condition.IsMet(other.State));
    Assert.False(new ResearchCompletedCondition { Project = MakeResearch() }.IsMet(campaign.State));
  }
}
