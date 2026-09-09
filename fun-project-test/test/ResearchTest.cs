using System;
using FunProject.Engineering;
using FunProject.GameState;
using FunProject.Research;
using FunProject.Strategic;
using GdUnit4;
using SysColGeneric = System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ResearchTest
{
  [TestCase]
  public void ResearchUnlocksManufacturingBeforeBroadcastWithoutGrantingStock()
  {
    var item = MakeItemData();
    var project = MakeResearch(manufacturingUnlocks: [item]);
    var dependent = MakeResearch("Dependent", condition: new ResearchCompletedCondition { Project = project });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project, dependent]));
    var session = campaign.Session;
    Assert.Equal(2, session.GetResearchProjects().Count);
    Assert.Equal(1, session.GetAvailableResearchProjects().Count);
    Assert.Equal(ManufacturingStartFailure.UnknownItem, session.StartManufacturing(item).RequireLeft());
    session.EventCommitted += e =>
    {
      if (e is ResearchStarted started)
      {
        Assert.Equal(Some(started.Job), session.ActiveResearch);
        Assert.Equal(campaign.State.Research.ActiveJob, session.ActiveResearch);
      }
      if (e is ResearchCompleted completed)
      {
        Assert.Equal(project, completed.Job.Project);
        Assert.True(session.ActiveResearch.IsNone);
        Assert.True(campaign.State.Research.IsCompleted(project));
        Assert.Equal(dependent, session.GetAvailableResearchProjects()[0]);
        Assert.Equal(item, session.GetManufacturingOptions()[0].Project.Item);
        Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
      }
    };
    var job = session.StartResearch(project).RequireRight();
    Assert.Equal(0L, job.StartedAtTick);
    Assert.Equal(1440L, job.CompletesAtTick);
    campaign.AdvanceTicks(1439);
    Assert.Equal(Some(job), session.ActiveResearch);
    Assert.Equal(0, session.GetManufacturingOptions().Count);
    campaign.AdvanceTicks(1);
    Assert.Equal(job, campaign.Events.SingleEvent<ResearchCompleted>().Job);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    session.StartManufacturing(item).RequireRight();
    campaign.AdvanceTicks(1440);
    Assert.True(campaign.State.Armory.TryWithdrawItem(item).IsSome);
    Assert.Equal(1, campaign.Events.EventsOf<ResearchCompleted>().Length);
  }

  [TestCase]
  public void ResearchCompletionRegistersManufacturingOptionsInCompletionOrder()
  {
    var firstItem = MakeItemData("First item");
    var secondItem = MakeItemData("Second item");
    var firstProject = MakeResearch("First project", manufacturingUnlocks: [firstItem]);
    var secondProject = MakeResearch("Second project", manufacturingUnlocks: [secondItem]);
    using var campaign = new GeoscapeFixture(MakeStart(
      researchProjects: [firstProject, secondProject]));

    campaign.Session.StartResearch(secondProject).RequireRight();
    campaign.AdvanceTicks(1440);
    campaign.Session.StartResearch(firstProject).RequireRight();
    campaign.AdvanceTicks(1440);

    var options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(2, options.Count);
    Assert.Equal(secondItem, options[0].Project.Item);
    Assert.Equal(firstItem, options[1].Project.Item);
  }

  [TestCase]
  public void LiveAvailabilityRelocksWhilePausedButDoesNotInterruptStartedResearch()
  {
    var key = MakeItemData("Key");
    var condition = new ResearchTestCondition { Predicate = state => state.Armory.HasAvailableItem(key) };
    var first = MakeResearch("First", condition: condition);
    var second = MakeResearch("Second", condition: condition);
    using var campaign = new GeoscapeFixture(MakeStart(armory: [key], researchProjects: [first, second]));
    var session = campaign.Session;
    Assert.Equal(TimeSpeed.Paused, session.Speed);
    var snapshot = session.GetAvailableResearchProjects();
    Assert.Equal(2, snapshot.Count);
    campaign.State.Armory.TryWithdrawItem(key).RequireSome();
    Assert.Equal(0, session.GetAvailableResearchProjects().Count);
    Assert.Equal(ResearchStartFailure.Locked, session.StartResearch(first).RequireLeft());
    Assert.Equal(0, campaign.Events.Count);
    campaign.State.Armory.AddStock(key, 1);
    Assert.Equal(2, session.GetAvailableResearchProjects().Count);
    var job = session.StartResearch(first).RequireRight();
    Assert.Equal(second, session.GetAvailableResearchProjects()[0]);
    campaign.State.Armory.TryWithdrawItem(key).RequireSome();
    Assert.Equal(0, session.GetAvailableResearchProjects().Count);
    Assert.Equal(2, snapshot.Count);
    campaign.AdvanceTicks(1440);
    Assert.Equal(job, campaign.Events.SingleEvent<ResearchCompleted>().Job);
    Assert.Equal(ResearchStartFailure.Locked, session.StartResearch(second).RequireLeft());
    Assert.Equal(ResearchStartFailure.AlreadyCompleted, session.StartResearch(first).RequireLeft());
  }

  [TestCase]
  public void StartFailurePrecedencePreservesTheActiveJobAndEventWindow()
  {
    var completed = MakeResearch("Completed");
    var active = MakeResearch("Active");
    var locked = MakeResearch("Locked", condition: new NotResearchCondition { Child = new AlwaysResearchCondition() });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [completed, active, locked]));
    var session = campaign.Session;
    session.StartResearch(completed).RequireRight();
    campaign.AdvanceTicks(1440);
    session.StartResearch(active).RequireRight();
    var job = session.ActiveResearch;
    campaign.ClearEvents();
    Assert.Equal(ResearchStartFailure.UnknownProject, session.StartResearch(MakeResearch("Active")).RequireLeft());
    Assert.Equal(ResearchStartFailure.Busy, session.StartResearch(active).RequireLeft());
    Assert.Equal(ResearchStartFailure.Busy, session.StartResearch(completed).RequireLeft());
    Assert.Equal(ResearchStartFailure.Busy, session.StartResearch(locked).RequireLeft());
    Assert.Equal(job, session.ActiveResearch);
    Assert.True(campaign.State.Research.IsCompleted(completed));
    Assert.False(campaign.State.Research.IsCompleted(active));
    Assert.False(campaign.State.Research.IsCompleted(locked));
    Assert.Equal(0, campaign.Events.Count);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
  }

  [TestCase]
  public void ConditionExceptionsAndNullArgumentsPropagateBeforeMutation()
  {
    var failure = new InvalidOperationException("Condition failed");
    var project = MakeResearch(condition: new ResearchTestCondition { Predicate = _ => throw failure });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    var session = campaign.Session;
    Action[] operations = [() => session.GetAvailableResearchProjects(), () => session.StartResearch(project)];
    foreach (var operation in operations)
    {
      bool propagated = false;
      try { operation(); }
      catch (InvalidOperationException error) when (ReferenceEquals(error, failure)) { propagated = true; }
      Assert.True(propagated);
    }
    Assert.Throws<ArgumentNullException>(() => session.StartResearch(null));
    Assert.Throws<ArgumentNullException>(() => campaign.State.Research.GetAvailableProjects(null));
    Assert.True(session.ActiveResearch.IsNone);
    Assert.False(campaign.State.Research.IsCompleted(project));
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void ZeroDayResearchStartedDuringTimeEventCompletesInThatTicksResearchPhase()
  {
    var project = MakeResearch(days: 0);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    campaign.Session.EventCommitted += e =>
    {
      if (e is TimeAdvanced { Tick: 1 })
      {
        var job = campaign.Session.StartResearch(project).RequireRight();
        Assert.Equal(1L, job.StartedAtTick);
        Assert.Equal(1L, job.CompletesAtTick);
        Assert.Equal(Some(job), campaign.Session.ActiveResearch);
        Assert.Equal(0, campaign.Events.EventsOf<ResearchCompleted>().Length);
      }
    };
    campaign.AdvanceTicks(1);
    Assert.Equal(3, campaign.Events.Count);
    Assert.True(campaign.Events[0] is TimeAdvanced);
    Assert.True(campaign.Events[1] is ResearchStarted);
    Assert.True(campaign.Events[2] is ResearchCompleted);
    Assert.True(campaign.Session.ActiveResearch.IsNone);
    Assert.True(campaign.State.Research.IsCompleted(project));
  }

  [TestCase(0U, 17L)]
  [TestCase(1U, 1457L)]
  [TestCase(uint.MaxValue, 6_184_752_904_817L)]
  public void CapturedDurationSchedulesFromSubmissionTick(uint days, long deadline)
  {
    var project = MakeResearch(days: days);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    project.DurationDays = 55;
    campaign.AdvanceTicks(17);
    var job = campaign.Session.StartResearch(project).RequireRight();
    Assert.Equal(17L, job.StartedAtTick);
    Assert.Equal(deadline, job.CompletesAtTick);
    Assert.True(campaign.Session.ActiveResearch.IsSome);
    if (days == 0)
    {
      Assert.Equal(0, campaign.Events.EventsOf<ResearchCompleted>().Length);
      campaign.AdvanceTicks(1);
      Assert.True(campaign.Session.ActiveResearch.IsNone);
      Assert.Equal(job, campaign.Events.SingleEvent<ResearchCompleted>().Job);
    }
  }

  [TestCase]
  public void DeadlineOverflowOccursBeforeStartingAndExactMaximumDeadlineIsSupported()
  {
    var project = MakeResearch();
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    campaign.State.Tick = long.MaxValue;
    Assert.Throws<OverflowException>(() => campaign.Session.StartResearch(project));
    Assert.True(campaign.Session.ActiveResearch.IsNone);
    Assert.Equal(0, campaign.Events.Count);
    campaign.State.Tick = long.MaxValue - 1440;
    var job = campaign.Session.StartResearch(project).RequireRight();
    Assert.Equal(long.MaxValue, job.CompletesAtTick);
    Assert.True(campaign.State.Research.CompleteIfDue(long.MaxValue - 1).IsNone);
    Assert.Equal(job, campaign.State.Research.CompleteIfDue(long.MaxValue).RequireSome().Job);
  }

  [TestCase]
  public void PauseAndPendingResolutionFreezeResearch()
  {
    var project = MakeResearch();
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project],
      timeline: [MakeScheduled(1, MakeEvent("Incident"))]));
    var job = campaign.Session.StartResearch(project).RequireRight();
    campaign.ClearEvents();
    campaign.Advance(100);
    Assert.Equal(0L, campaign.Session.Tick);
    Assert.Equal(0, campaign.Events.Count);
    campaign.AdvanceTicks(1);
    campaign.OpenResolution(campaign.ActiveEvent);
    campaign.ClearEvents();
    campaign.Advance(100);
    Assert.Equal(1L, campaign.Session.Tick);
    Assert.Equal(0, campaign.Events.Count);
    Assert.Equal(Some(job), campaign.Session.ActiveResearch);
    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    campaign.AdvanceTicks(1439);
    Assert.Equal(job, campaign.Events.SingleEvent<ResearchCompleted>().Job);
  }

  [TestCase]
  public void BothJobsCompleteAfterScheduleAndExpiryInDefinedOrder()
  {
    var initial = MakeItemData("Initial");
    var unlocked = MakeItemData("Unlocked");
    var project = MakeResearch(manufacturingUnlocks: [unlocked]);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [initial],
      researchProjects: [project], timeline: [MakeScheduled(1440, MakeEvent("Brief", expiresAfterTicks: 0))]));
    campaign.Session.StartManufacturing(initial).RequireRight();
    campaign.Session.StartResearch(project).RequireRight();
    campaign.AdvanceTicks(1439);
    campaign.ClearEvents();
    campaign.AdvanceTicks(1);
    Assert.Equal(5, campaign.Events.Count);
    Assert.True(campaign.Events[0] is TimeAdvanced);
    Assert.True(campaign.Events[1] is ScheduledEventFired);
    Assert.True(campaign.Events[2] is EventExpired);
    Assert.True(campaign.Events[3] is ManufacturingCompleted);
    Assert.True(campaign.Events[4] is ResearchCompleted);
    Assert.True(campaign.State.Armory.HasAvailableItem(initial));
    Assert.False(campaign.State.Armory.HasAvailableItem(unlocked));
    Assert.Equal(2, campaign.Session.GetManufacturingOptions().Count);
  }

  [TestCase]
  public void RebuiltSessionPreservesProgressAndLargeAdvanceCompletesExactlyAtDeadline()
  {
    var item = MakeItemData();
    var project = MakeResearch(manufacturingUnlocks: [item]);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    var job = campaign.Session.StartResearch(project).RequireRight();
    campaign.AdvanceTicks(7);
    campaign.ClearEvents();
    var rebuilt = new GeoscapeSession(campaign.State);
    Assert.Equal(Some(job), rebuilt.ActiveResearch);
    SysColGeneric.List<IGeoscapeEvent> events = [];
    long completedTick = -1;
    rebuilt.EventCommitted += e =>
    {
      events.Add(e);
      if (e is ResearchCompleted) completedTick = rebuilt.Tick;
    };
    rebuilt.ChangeSpeed(TimeSpeed.Normal);
    rebuilt.Advance(200);
    Assert.Equal(2007L, rebuilt.Tick);
    Assert.Equal(1440L, completedTick);
    Assert.Equal(job, events.SingleEvent<ResearchCompleted>().Job);
    Assert.Equal(0, campaign.Events.Count);
    var completed = new GeoscapeSession(campaign.State);
    Assert.True(completed.ActiveResearch.IsNone);
    Assert.True(campaign.State.Research.IsCompleted(project));
    Assert.Equal(ResearchStartFailure.AlreadyCompleted, completed.StartResearch(project).RequireLeft());
    Assert.Equal(1, completed.GetManufacturingOptions().Count);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
  }

  [TestCase]
  public void CompletedResearchAndManufacturingUnlocksSurviveConditionLossAndOverlap()
  {
    var key = MakeItemData("Key");
    var item = MakeItemData("Product");
    var first = MakeResearch("First", condition: new ResearchTestCondition
    { Predicate = state => state.Armory.HasAvailableItem(key) }, manufacturingUnlocks: [item]);
    var second = MakeResearch("Second", manufacturingUnlocks: [item, item]);
    using var campaign = new GeoscapeFixture(MakeStart(armory: [key], researchProjects: [first, second]));
    campaign.Session.StartResearch(first).RequireRight();
    campaign.AdvanceTicks(1440);
    campaign.State.Armory.TryWithdrawItem(key).RequireSome();
    Assert.True(campaign.State.Research.IsCompleted(first));
    Assert.Equal(ResearchStartFailure.AlreadyCompleted, campaign.Session.StartResearch(first).RequireLeft());
    campaign.Session.StartResearch(second).RequireRight();
    campaign.AdvanceTicks(1440);
    Assert.True(campaign.State.Research.IsCompleted(first));
    Assert.True(campaign.State.Research.IsCompleted(second));
    Assert.Equal(1, campaign.Session.GetManufacturingOptions().Count);
    Assert.False(campaign.State.Armory.HasAvailableItem(item));
    using var other = new GeoscapeFixture(MakeStart(armory: [key], researchProjects: [first, second]));
    Assert.False(other.State.Research.IsCompleted(first));
    Assert.Equal(ManufacturingStartFailure.UnknownItem, other.Session.StartManufacturing(item).RequireLeft());
  }
}
