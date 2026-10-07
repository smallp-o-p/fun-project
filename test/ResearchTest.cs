#nullable disable warnings
using System;
using FunProject.Engineering;
using FunProject.GameState;
using FunProject.Research;
using FunProject.Strategic;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ResearchTest
{
  [TestCase]
  public void JobStartsSpendSharedBalancesBeforeNotification()
  {
    var a = MakeCurrency("A");
    var b = MakeCurrency("B");
    var item = MakeItemData(manufacturingCosts: new() { [a] = 20, [b] = 2 });
    var project = MakeResearch(costs: new() { [a] = 30, [b] = 3 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item], researchProjects: [project],
      startingCurrencies: new() { [a] = 100, [b] = 10 }));
    campaign.Session.EventCommitted += fact =>
    {
      if (fact is ManufacturingStarted manufacturing)
      {
        Assert.Equal(Some(manufacturing.Job), campaign.Session.ActiveManufacturing);
        Assert.Equal(80L, campaign.State.GetCurrencyBalance(a));
        Assert.Equal(8L, campaign.State.GetCurrencyBalance(b));
      }
      if (fact is ResearchStarted research)
      {
        Assert.Equal(Some(research.Job), campaign.Session.ActiveResearch);
        Assert.Equal(50L, campaign.State.GetCurrencyBalance(a));
        Assert.Equal(5L, campaign.State.GetCurrencyBalance(b));
      }
    };
    campaign.Session.StartManufacturing(item).RequireRight();
    campaign.Session.StartResearch(project).RequireRight();
    campaign.AdvanceTicks(100);
    var rebuilt = new GeoscapeSession(campaign.State);
    rebuilt.ChangeSpeed(TimeSpeed.Normal);
    rebuilt.Advance(1340 * 0.1);
    Assert.True(rebuilt.ActiveManufacturing.IsNone);
    Assert.True(rebuilt.ActiveResearch.IsNone);
    Assert.True(campaign.State.Research.IsCompleted(project));
    Assert.Equal(1, campaign.State.Armory.ItemStock()[0].Remaining);
    Assert.Equal(50L, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(5L, campaign.State.GetCurrencyBalance(b));
  }

  [TestCase(false)]
  [TestCase(true)]
  public void InsufficientCurrenciesPreserveJobsBalancesAndEvents(bool research)
  {
    var a = MakeCurrency("A");
    var b = MakeCurrency("B");
    var item = MakeItemData(manufacturingCosts: new() { [a] = 7, [b] = 2 });
    var old = MakeResearch("Old");
    var project = MakeResearch(costs: new() { [a] = 7, [b] = 2 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item], researchProjects: [old, project],
      startingCurrencies: new() { [a] = 10, [b] = 1 }));
    var active = campaign.Session.StartResearch(old).RequireRight();
    campaign.AdvanceTicks(100);
    campaign.ClearEvents();
    if (research)
      Assert.Equal(ResearchStartFailure.InsufficientCurrencies, campaign.Session.StartResearch(project).RequireLeft());
    else
      Assert.Equal(ManufacturingStartFailure.InsufficientCurrencies, campaign.Session.StartManufacturing(item).RequireLeft());
    Assert.Equal(Some(active), campaign.Session.ActiveResearch);
    Assert.True(campaign.Session.ActiveManufacturing.IsNone);
    Assert.Equal(10L, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(1L, campaign.State.GetCurrencyBalance(b));
    Assert.Equal(0, campaign.Events.Count);
    Assert.Equal(1, campaign.Session.GetAvailableResearchProjects().Count);
    Assert.Equal(1, campaign.Session.GetManufacturingOptions().Count);
  }

  [TestCase]
  public void ResearchReplacementPaysAgainWithoutRefund()
  {
    var currency = MakeCurrency();
    var first = MakeResearch("A", costs: new() { [currency] = 30 });
    var second = MakeResearch("B", costs: new() { [currency] = 50 });
    var unaffordable = MakeResearch("Too expensive", costs: new() { [currency] = 280 });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [first, second, unaffordable],
      startingCurrencies: new() { [currency] = 300 }));
    var old = campaign.Session.StartResearch(first).RequireRight();
    campaign.AdvanceTicks(100);
    campaign.ClearEvents();
    Assert.Equal(ResearchStartFailure.InsufficientCurrencies, campaign.Session.StartResearch(unaffordable).RequireLeft());
    Assert.Equal(ResearchStartFailure.Busy, campaign.Session.StartResearch(first).RequireLeft());
    Assert.Equal(Some(old), campaign.Session.ActiveResearch);
    Assert.Equal(270L, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(0, campaign.Events.Count);
    campaign.Session.StartResearch(second).RequireRight();
    Assert.Equal(220L, campaign.State.GetCurrencyBalance(currency));
    campaign.AdvanceTicks(100);
    var restarted = campaign.Session.StartResearch(first).RequireRight();
    Assert.Equal(200L, restarted.StartedAtTick);
    Assert.Equal(1640L, restarted.CompletesAtTick);
    Assert.Equal(190L, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(2, campaign.Events.EventsOf<ResearchStarted>().Length);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void StartNotificationExceptionDoesNotReplayPayment(bool research)
  {
    var currency = MakeCurrency();
    var old = MakeResearch("Old");
    var project = MakeResearch(costs: new() { [currency] = 7 });
    var item = MakeItemData(manufacturingCosts: new() { [currency] = 7 });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [old, project], manufacturableItems: [item],
      startingCurrencies: new() { [currency] = 10 }));
    campaign.Session.StartResearch(old).RequireRight();
    campaign.AdvanceTicks(10);
    campaign.Session.EventCommitted += fact =>
    {
      if (fact is ResearchStarted or ManufacturingStarted)
        throw new InvalidOperationException("Subscriber failed");
    };
    if (research)
    {
      Assert.Throws<InvalidOperationException>(() => campaign.Session.StartResearch(project));
      var job = campaign.Session.ActiveResearch.RequireSome();
      Assert.Equal(project, job.Project);
      Assert.Equal(10L, job.StartedAtTick);
      Assert.Equal(ResearchStartFailure.Busy, campaign.Session.StartResearch(project).RequireLeft());
      Assert.Equal(Some(job), campaign.Session.ActiveResearch);
    }
    else
    {
      Assert.Throws<InvalidOperationException>(() => campaign.Session.StartManufacturing(item));
      var job = campaign.Session.ActiveManufacturing.RequireSome();
      Assert.Equal(item, job.Project.Item);
      Assert.Equal(ManufacturingStartFailure.Busy, campaign.Session.StartManufacturing(item).RequireLeft());
      Assert.Equal(Some(job), campaign.Session.ActiveManufacturing);
    }
    Assert.Equal(3L, campaign.State.GetCurrencyBalance(currency));
  }

  [TestCase]
  public void FailedResearchUnlockRetainsPaymentAndCompletionWithoutPartialRegistration()
  {
    var currency = MakeCurrency();
    var first = MakeItemData("Valid");
    var malformed = MakeItemData("Malformed", manufacturingCosts: new() { [currency] = 7 });
    var project = MakeResearch(days: 0, manufacturingUnlocks: [first, malformed], costs: new() { [currency] = 7 });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project], startingCurrencies: new() { [currency] = 10 }));
    malformed.ManufacturingCosts[currency] = -1;
    campaign.Session.StartResearch(project).RequireRight();
    Assert.Throws<ArgumentOutOfRangeException>(() => campaign.AdvanceTicks(1));
    Assert.True(campaign.Session.ActiveResearch.IsNone);
    Assert.True(campaign.State.Research.IsCompleted(project));
    Assert.Equal(0, campaign.Session.GetManufacturingOptions().Count);
    Assert.Equal(0, campaign.Events.EventsOf<ResearchCompleted>().Length);
    campaign.AdvanceTicks(1);
    Assert.Equal(3L, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(0, campaign.Session.GetManufacturingOptions().Count);
  }

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
    var currency = MakeCurrency();
    var completed = MakeResearch("Completed", costs: new() { [currency] = 10 });
    var active = MakeResearch("Active", costs: new() { [currency] = 10 });
    var locked = MakeResearch("Locked", condition: new NotResearchCondition { Child = new AlwaysResearchCondition() }, costs: new() { [currency] = 30 });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [completed, active, locked], startingCurrencies: new() { [currency] = 20 }));
    var session = campaign.Session;
    session.StartResearch(completed).RequireRight();
    campaign.AdvanceTicks(1440);
    session.StartResearch(active).RequireRight();
    var job = session.ActiveResearch;
    campaign.ClearEvents();
    (ResearchProject Project, ResearchStartFailure Failure)[] requests =
    [
      (MakeResearch("Active"), ResearchStartFailure.UnknownProject),
      (active, ResearchStartFailure.Busy),
      (completed, ResearchStartFailure.AlreadyCompleted),
      (locked, ResearchStartFailure.Locked),
    ];
    foreach (var (project, failure) in requests)
    {
      Assert.Equal(failure, session.StartResearch(project).RequireLeft());
      Assert.Equal(job, session.ActiveResearch);
      Assert.Equal(0, campaign.Events.Count);
    }
    Assert.True(campaign.State.Research.IsCompleted(completed));
    Assert.False(campaign.State.Research.IsCompleted(active));
    Assert.False(campaign.State.Research.IsCompleted(locked));
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(0L, campaign.State.GetCurrencyBalance(currency));
  }

  [TestCase]
  public void SwitchingDiscardsProgressAndOnlyTheReplacementCanComplete()
  {
    var firstItem = MakeItemData("First reward");
    var secondItem = MakeItemData("Second reward");
    var first = MakeResearch("First", manufacturingUnlocks: [firstItem]);
    var second = MakeResearch("Second", days: 2, manufacturingUnlocks: [secondItem]);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [first, second]));
    var session = campaign.Session;
    session.StartResearch(first).RequireRight();
    campaign.AdvanceTicks(100);
    var replacement = session.StartResearch(second).RequireRight();
    Assert.Equal(100L, replacement.StartedAtTick);
    Assert.Equal(2980L, replacement.CompletesAtTick);
    Assert.Equal(Some(replacement), session.ActiveResearch);
    Assert.Equal(first, session.GetAvailableResearchProjects()[0]);
    campaign.AdvanceTicks(1340);
    Assert.Equal(0, campaign.Events.EventsOf<ResearchCompleted>().Length);
    Assert.Equal(0, session.GetManufacturingOptions().Count);

    var restarted = session.StartResearch(first).RequireRight();
    Assert.Equal(1440L, restarted.StartedAtTick);
    Assert.Equal(2880L, restarted.CompletesAtTick);
    campaign.AdvanceTicks(1439);
    Assert.Equal(Some(restarted), session.ActiveResearch);
    campaign.AdvanceTicks(101);
    Assert.Equal(restarted, campaign.Events.SingleEvent<ResearchCompleted>().Job);
    Assert.True(campaign.State.Research.IsCompleted(first));
    Assert.False(campaign.State.Research.IsCompleted(second));
    Assert.Equal(firstItem, session.GetManufacturingOptions().AsValueEnumerable().Single().Project.Item);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(3, campaign.Events.EventsOf<ResearchStarted>().Length);
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
    var currency = MakeCurrency();
    var project = MakeResearch(costs: new() { [currency] = 7 });
    var replacement = MakeResearch("Replacement", costs: new() { [currency] = 7 });
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project, replacement], startingCurrencies: new() { [currency] = 14 }));
    campaign.State.Tick = long.MaxValue;
    Assert.Throws<OverflowException>(() => campaign.Session.StartResearch(project));
    Assert.True(campaign.Session.ActiveResearch.IsNone);
    Assert.Equal(0, campaign.Events.Count);
    Assert.Equal(14L, campaign.State.GetCurrencyBalance(currency));
    campaign.State.Tick = long.MaxValue - 1440;
    var job = campaign.Session.StartResearch(project).RequireRight();
    Assert.Equal(long.MaxValue, job.CompletesAtTick);
    campaign.State.Tick = long.MaxValue;
    campaign.ClearEvents();
    Assert.Throws<OverflowException>(() => campaign.Session.StartResearch(replacement));
    Assert.Equal(Some(job), campaign.Session.ActiveResearch);
    Assert.Equal(0, campaign.Events.Count);
    Assert.Equal(7L, campaign.State.GetCurrencyBalance(currency));
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
