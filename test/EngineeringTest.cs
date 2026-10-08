using System;
using FunProject.Engineering;
using FunProject.GameState;
using FunProject.Currencies;
using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class EngineeringTest
{
  [TestCase(false)]
  [TestCase(true)]
  public void FirstManufactureCreatesShelfOnlyAtCompletion(bool unlimited)
  {
    var item = MakeItemData(unlimited: unlimited);
    var itemRecipe = MakeManufacturingRecipe(item);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    var armory = campaign.State.Armory;
    var session = campaign.Session;

    Assert.Equal(0, armory.ItemStock().Count);
    Assert.True(armory.TryGetItemStock(item).IsNone);
    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    campaign.AdvanceTicks(1439);
    Assert.True(armory.TryGetItemStock(item).IsNone);
    Assert.True(armory.TryWithdrawItem(item).IsNone);

    campaign.AdvanceTicks(1);
    Assert.Equal(1, armory.ItemStock().Count);
    Assert.True(armory.TryGetItemStock(item).RequireSome().Available);
    Assert.True(armory.TryWithdrawItem(item).IsSome);
    Assert.True(armory.TryGetItemStock(item).IsSome);
    Assert.Equal(unlimited, armory.HasAvailableItem(item));
    Assert.Equal(unlimited ? 0 : 1, session.GetManufacturingOptions().Count);
  }

  [TestCase]
  public void UnlimitedProductionCreatesSupplyOnce()
  {
    var item = MakeItemData("Supply", unlimited: true);
    var itemRecipe = MakeManufacturingRecipe(item, days: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    var state = campaign.State;
    var session = campaign.Session;
    campaign.ClearEvents();
    session.EventCommitted += e =>
    {
      if (e is ManufacturingStarted started)
      {
        Assert.Equal(Some(started.Job), session.ActiveManufacturing);
        Assert.Equal(state.Engineering.ActiveJob, session.ActiveManufacturing);
        Assert.False(state.Armory.HasAvailableItem(item));
        Assert.Equal(1, session.GetManufacturingOptions().Count);
      }
      if (e is ManufacturingCompleted completed)
      {
        Assert.Equal(item, completed.Job.Project.Item);
        Assert.True(state.Armory.HasAvailableItem(item));
        Assert.True(session.ActiveManufacturing.IsNone);
        Assert.Equal(0, session.GetManufacturingOptions().Count);
      }
    };

    Assert.Equal(1, session.GetManufacturingOptions().Count);
    var result = session.StartManufacturing(itemRecipe);
    Assert.True(result.IsRight);
    var job = session.ActiveManufacturing.RequireSome();
    Assert.Equal(Right<ManufacturingStartFailure, ManufacturingJob>(job), result);
    Assert.Equal(1440L, job.CompletesAtTick);
    Assert.False(state.Armory.TryWithdrawItem(item).IsSome);
    campaign.AdvanceTicks(1);
    Assert.Equal(Some(job), session.ActiveManufacturing);
    campaign.AdvanceTicks(1439);
    var first = state.Armory.TryWithdrawItem(item);
    var second = state.Armory.TryWithdrawItem(item);
    Assert.True(first.IsSome);
    Assert.True(second.IsSome);
    Assert.False(first == second);
    Assert.Equal(0, session.GetManufacturingOptions().Count);
    Assert.Equal(ManufacturingStartFailure.AlreadyAvailable, session.StartManufacturing(itemRecipe).RequireLeft());
    campaign.AdvanceTicks(5);
    Assert.Equal(1, campaign.Events.AsValueEnumerable().Count(e => e is ManufacturingStarted));
    Assert.Equal(1, campaign.Events.AsValueEnumerable().Count(e => e is ManufacturingCompleted));
    Assert.Equal(new ManufacturingStarted(job), campaign.Events[0]);
    Assert.True(campaign.Events[1] is TimeAdvanced);
    Assert.Equal(new ManufacturingCompleted(job), campaign.Events.SingleEvent<ManufacturingCompleted>());
    Assert.True(session.ActiveManufacturing.IsNone);
  }

  [TestCase]
  public void ScarceProductionAddsOnePerCompletedJob()
  {
    var currency = MakeCurrency();
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, days: 1, costs: new() { [currency] = 7 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe], startingCurrencies: new() { [currency] = 14 }));
    var state = campaign.State;
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    Assert.True(state.Armory.TryGetItemStock(item).IsNone);
    campaign.AdvanceTicks(1440);
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining);
    Assert.Equal(7UL, state.GetCurrencyBalance(currency));
    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    Assert.Equal(0UL, state.GetCurrencyBalance(currency));
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining);
    campaign.AdvanceTicks(1440);
    Assert.Equal(2, state.Armory.ItemStock()[0].Remaining);
    Assert.Equal(1, session.GetManufacturingOptions().Count);
    campaign.AdvanceTicks(5);
    Assert.Equal(2, state.Armory.ItemStock()[0].Remaining);
  }

  [TestCase]
  public void InitiallySuppliedUnlimitedItemHasNoOptionAndRejectsWithoutEffects()
  {
    var item = MakeItemData(unlimited: true);
    var itemRecipe = MakeManufacturingRecipe(item);
    using var campaign = new GeoscapeFixture(MakeStart(armory: [item], manufacturingRecipes: [itemRecipe]));
    var state = campaign.State;
    var session = campaign.Session;
    campaign.ClearEvents();
    var stock = state.Armory.ItemStock()[0];

    Assert.Equal(0, session.GetManufacturingOptions().Count);
    Assert.Equal(ManufacturingStartFailure.AlreadyAvailable, session.StartManufacturing(itemRecipe).RequireLeft());
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(stock, state.Armory.ItemStock()[0]);
    Assert.Equal(0, campaign.Events.Count);
    Assert.True(state.Armory.TryWithdrawItem(item).IsSome);
    Assert.True(state.Armory.TryWithdrawItem(item).IsSome);
  }

  [TestCase]
  public void UnknownRecipePrecedesBusyAndUsesCatalogResourceIdentity()
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item);
    var sameName = MakeItemData();
    var sameNameRecipe = MakeManufacturingRecipe(sameName);
    var armoryOnly = MakeItemData("Armory only");
    var armoryOnlyRecipe = MakeManufacturingRecipe(armoryOnly);
    using var campaign = new GeoscapeFixture(MakeStart(armory: [armoryOnly], manufacturingRecipes: [itemRecipe]));
    var state = campaign.State;
    var session = campaign.Session;
    campaign.ClearEvents();
    var stock = state.Armory.ItemStock();

    Assert.Equal(ManufacturingStartFailure.UnknownRecipe, session.StartManufacturing(sameNameRecipe).RequireLeft());
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe, session.StartManufacturing(armoryOnlyRecipe).RequireLeft());
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(0, campaign.Events.Count);
    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    var active = session.ActiveManufacturing;
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe, session.StartManufacturing(sameNameRecipe).RequireLeft());
    Assert.Equal(active, session.ActiveManufacturing);
    Assert.Equal(1, campaign.Events.Count);
    Assert.Equal(1, state.Armory.ItemStock().Count);
    Assert.Equal(stock[0], state.Armory.ItemStock()[0]);
    Assert.True(state.Armory.TryGetItemStock(item).IsNone);
    Assert.Equal(1, session.GetManufacturingOptions().Count);
  }

  [TestCase]
  public void BusyPrecedesAlreadyAvailableWithoutReplacingJob()
  {
    var currency = MakeCurrency();
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, costs: new() { [currency] = 10 });
    var other = MakeItemData("Other");
    var otherRecipe = MakeManufacturingRecipe(other, costs: new() { [currency] = 20 });
    var supplied = MakeItemData("Supplied", unlimited: true);
    var suppliedRecipe = MakeManufacturingRecipe(supplied, costs: new() { [currency] = 20 });
    using var campaign = new GeoscapeFixture(MakeStart(armory: [supplied],
      manufacturingRecipes: [itemRecipe, otherRecipe, suppliedRecipe], startingCurrencies: new() { [currency] = 10 }));
    var state = campaign.State;
    var session = campaign.Session;
    campaign.ClearEvents();

    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    var active = session.ActiveManufacturing;
    Assert.Equal(ManufacturingStartFailure.Busy, session.StartManufacturing(itemRecipe).RequireLeft());
    Assert.Equal(ManufacturingStartFailure.Busy, session.StartManufacturing(otherRecipe).RequireLeft());
    Assert.Equal(ManufacturingStartFailure.Busy, session.StartManufacturing(suppliedRecipe).RequireLeft());
    Assert.Equal(active, session.ActiveManufacturing);
    Assert.Equal(1, campaign.Events.Count);
    Assert.Equal(2, session.GetManufacturingOptions().Count);
    Assert.False(state.Armory.HasAvailableItem(item));
    Assert.False(state.Armory.HasAvailableItem(other));
    Assert.True(state.Armory.HasAvailableItem(supplied));
    Assert.Equal(0UL, state.GetCurrencyBalance(currency));
    campaign.AdvanceTicks(1440);
    campaign.ClearEvents();
    Assert.Equal(ManufacturingStartFailure.AlreadyAvailable, session.StartManufacturing(suppliedRecipe).RequireLeft());
    Assert.Equal(0UL, state.GetCurrencyBalance(currency));
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void OptionsPreserveAuthoredOrderWhileBusyAndReadCurrentArmorySnapshots()
  {
    var first = MakeItemData("First");
    var firstRecipe = MakeManufacturingRecipe(first);
    var unlimited = MakeItemData("Unlimited", unlimited: true);
    var unlimitedRecipe = MakeManufacturingRecipe(unlimited, days: 1);
    var supplied = MakeItemData("Supplied", unlimited: true);
    var suppliedRecipe = MakeManufacturingRecipe(supplied);
    var last = MakeItemData("Last");
    var lastRecipe = MakeManufacturingRecipe(last);
    using var campaign = new GeoscapeFixture(MakeStart(armory: [last, supplied],
      manufacturingRecipes: [firstRecipe, unlimitedRecipe, suppliedRecipe, lastRecipe]));
    var state = campaign.State;
    var session = campaign.Session;
    var options = session.GetManufacturingOptions();
    Assert.Equal(3, options.Count);
    Assert.Equal(first, options[0].Project.Item);
    Assert.Equal(unlimited, options[1].Project.Item);
    Assert.Equal(last, options[2].Project.Item);
    Assert.True(state.Armory.TryGetItemStock(first).IsNone);
    Assert.Equal(0, options[0].Remaining);
    Assert.True(options[1].Project.Item.UnlimitedStock);
    Assert.Equal(0, options[1].Remaining);
    Assert.Equal(1, options[2].Remaining);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IList<ManufacturingOption>)options).Clear());
    Assert.Equal(3, session.GetManufacturingOptions().Count);
    Assert.Equal(1, state.Armory.TryGetItemStock(last).RequireSome().Remaining);

    Assert.True(session.StartManufacturing(unlimitedRecipe).IsRight);
    Assert.Equal(3, session.GetManufacturingOptions().Count);
    Assert.True(state.Armory.TryWithdrawItem(last).IsSome);
    var busyOptions = session.GetManufacturingOptions();
    Assert.Equal(first, busyOptions[0].Project.Item);
    Assert.Equal(unlimited, busyOptions[1].Project.Item);
    Assert.Equal(last, busyOptions[2].Project.Item);
    Assert.Equal(0, busyOptions[2].Remaining);
    campaign.AdvanceTicks(1440);
    Assert.Equal(2, session.GetManufacturingOptions().Count);
    Assert.Equal(first, session.GetManufacturingOptions()[0].Project.Item);
    Assert.Equal(last, session.GetManufacturingOptions()[1].Project.Item);
    Assert.Equal(3, options.Count);
    Assert.Equal(0, options[1].Remaining);
    Assert.Equal(1, options[2].Remaining);
  }

  [TestCase]
  public void LargeAdvanceCompletesJobOnceAtItsDeadlineWithStockAlreadyVisible()
  {
    var product = MakeItemData("Product");
    var productRecipe = MakeManufacturingRecipe(product, days: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [productRecipe]));
    var state = campaign.State;
    var session = campaign.Session;
    campaign.AdvanceTicks(3);
    campaign.ClearEvents();
    var job = session.StartManufacturing(productRecipe).RequireRight();
    Assert.Equal(3L, job.StartedAtTick);
    Assert.Equal(1443L, job.CompletesAtTick);
    long completedAt = -1;
    session.EventCommitted += e =>
    {
      if (e is ManufacturingCompleted manufacturing)
      {
        completedAt = session.Tick;
        Assert.Equal(job, manufacturing.Job);
        Assert.Equal(1, state.Armory.TryGetItemStock(product).RequireSome().Remaining);
        Assert.True(session.ActiveManufacturing.IsNone);
      }
    };

    session.Advance(200.0);
    Assert.Equal(2003L, session.Tick);
    Assert.Equal(1443L, completedAt);
    Assert.Equal(2002, campaign.Events.Count);
    Assert.Equal(2000, campaign.Events.EventsOf<TimeAdvanced>().Length);
    Assert.Equal(job, campaign.Events.SingleEvent<ManufacturingCompleted>().Job);
    Assert.True(campaign.Events[0] is ManufacturingStarted);
    Assert.True(campaign.Events[1] is TimeAdvanced);
    Assert.True(campaign.Events[1440] is TimeAdvanced);
    Assert.Equal(new ManufacturingCompleted(job), campaign.Events[1441]);
    campaign.AdvanceTicks(5);
    Assert.Equal(1, campaign.Events.EventsOf<ManufacturingCompleted>().Length);
    Assert.Equal(1, state.Armory.TryGetItemStock(product).RequireSome().Remaining);
  }

  [TestCase]
  public void PausedAdvancePreservesJobWithoutAccumulatingProgress()
  {
    var product = MakeItemData("Product");
    var productRecipe = MakeManufacturingRecipe(product, days: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [productRecipe]));
    var state = campaign.State;
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(productRecipe).IsRight);
    var job = session.ActiveManufacturing;
    campaign.ClearEvents();

    session.Advance(100);
    Assert.Equal(0L, session.Tick);
    Assert.Equal(job, session.ActiveManufacturing);
    Assert.Equal(0, campaign.Events.Count);
    campaign.AdvanceTicks(1);
    Assert.Equal(1L, session.Tick);
    Assert.Equal(job, session.ActiveManufacturing);
    session.ChangeSpeed(TimeSpeed.Paused);
    session.Advance(100);
    Assert.Equal(1L, session.Tick);
    Assert.Equal(job, session.ActiveManufacturing);
    Assert.False(state.Armory.HasAvailableItem(product));
    Assert.Equal(1, campaign.Events.Count);
    campaign.AdvanceTicks(1);
    Assert.Equal(2L, session.Tick);
    Assert.Equal(job, session.ActiveManufacturing);
    Assert.Equal(2, campaign.Events.Count);
    campaign.AdvanceTicks(1438);
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(1, state.Armory.TryGetItemStock(product).RequireSome().Remaining);
  }

  [TestCase]
  public void PendingResolutionFreezesJobUntilCompleted()
  {
    var product = MakeItemData("Product");
    var productRecipe = MakeManufacturingRecipe(product, days: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [productRecipe],
      timeline: [MakeScheduled(1, MakeEvent("Incident"))]));
    var state = campaign.State;
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(productRecipe).IsRight);
    campaign.AdvanceTicks(1);
    session.OpenResolution(session.ActiveEvents[0]);
    var job = session.ActiveManufacturing;
    campaign.ClearEvents();
    session.Advance(100);
    Assert.Equal(1L, session.Tick);
    Assert.Equal(job, session.ActiveManufacturing);
    Assert.False(state.Armory.HasAvailableItem(product));
    Assert.Equal(0, campaign.Events.Count);
    session.CompleteResolution(ResolutionOutcome.Acknowledged);
    campaign.AdvanceTicks(1);
    Assert.Equal(2L, session.Tick);
    Assert.Equal(job, session.ActiveManufacturing);
    campaign.AdvanceTicks(1438);
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(1, state.Armory.TryGetItemStock(product).RequireSome().Remaining);
  }

  [TestCase]
  public void RebuiltSessionContinuesJobAndRetainsSupply()
  {
    var product = MakeItemData("Product", unlimited: true);
    var productRecipe = MakeManufacturingRecipe(product, days: 1);
    var state = new GameState(MakeStart(manufacturingRecipes: [productRecipe]));
    var original = new GeoscapeSession(state);
    Assert.True(original.StartManufacturing(productRecipe).IsRight);
    var job = original.ActiveManufacturing;
    original.ChangeSpeed(TimeSpeed.Normal);
    original.Advance(7 * 0.1);
    var rebuilt = new GeoscapeSession(state);
    Assert.Equal(7L, rebuilt.Tick);
    Assert.Equal(job, rebuilt.ActiveManufacturing);
    Assert.Equal(state.Engineering.ActiveJob, rebuilt.ActiveManufacturing);
    SysColGeneric.List<IGeoscapeEvent> events = [];
    rebuilt.EventCommitted += events.Add;
    rebuilt.ChangeSpeed(TimeSpeed.Normal);
    rebuilt.Advance(1433 * 0.1);
    Assert.Equal(1440L, rebuilt.Tick);
    Assert.Equal(1, events.EventsOf<ManufacturingCompleted>().Length);
    var completed = new GeoscapeSession(state);
    Assert.True(completed.ActiveManufacturing.IsNone);
    Assert.Equal(0, completed.GetManufacturingOptions().Count);
    Assert.True(state.Armory.TryWithdrawItem(product).IsSome);
    Assert.Equal(ManufacturingStartFailure.AlreadyAvailable, completed.StartManufacturing(productRecipe).RequireLeft());
  }

  [TestCase]
  public void CompletionTickOverflowThrowsBeforeAssigningJobOrChangingStock()
  {
    var currency = MakeCurrency();
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, costs: new() { [currency] = 7 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe], startingCurrencies: new() { [currency] = 7 }));
    var state = campaign.State;
    var session = campaign.Session;

    state.Tick = long.MaxValue;
    Assert.Throws<OverflowException>(() => session.StartManufacturing(itemRecipe));
    Assert.True(state.Engineering.ActiveJob.IsNone);
    Assert.False(state.Armory.HasAvailableItem(item));
    Assert.Equal(0, campaign.Events.Count);

    Assert.Equal(7UL, state.GetCurrencyBalance(currency));

    state.Tick = long.MaxValue - 1440;
    var job = session.StartManufacturing(itemRecipe).RequireRight();
    Assert.Equal(0UL, state.GetCurrencyBalance(currency));
    Assert.Equal(long.MaxValue - 1440, job.StartedAtTick);
    Assert.Equal(long.MaxValue, job.CompletesAtTick);
    Assert.True(state.Engineering.CompleteIfDue(long.MaxValue - 1).IsNone);
    Assert.Equal(new ManufacturingCompleted(job), state.Engineering.CompleteIfDue(long.MaxValue).RequireSome());
    Assert.True(state.Engineering.CompleteIfDue(long.MaxValue).IsNone);
    Assert.False(state.Armory.HasAvailableItem(item));
  }

  [TestCase]
  public void FailedCompletionEffectPropagatesWithoutBroadcastingOrRetrying()
  {
    var currency = MakeCurrency();
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, days: 1, costs: new() { [currency] = 7 });
    using var campaign = new GeoscapeFixture(MakeStart(armory: [item], manufacturingRecipes: [itemRecipe], startingCurrencies: new() { [currency] = 10 }));
    var state = campaign.State;
    var session = campaign.Session;
    state.Armory.AddStock(item, int.MaxValue - 1);
    campaign.ClearEvents();
    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    Assert.Throws<OverflowException>(() => campaign.AdvanceTicks(1440));
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(int.MaxValue, state.Armory.ItemStock()[0].Remaining);
    Assert.Equal(0, campaign.Events.AsValueEnumerable().Count(e => e is ManufacturingCompleted));

    Assert.True(state.Armory.TryWithdrawItem(item).IsSome);
    campaign.AdvanceTicks(1);
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(int.MaxValue - 1, state.Armory.ItemStock()[0].Remaining);
    Assert.Equal(0, campaign.Events.EventsOf<ManufacturingCompleted>().Length);
    Assert.Equal(3UL, state.GetCurrencyBalance(currency));
  }
}
