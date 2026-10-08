using FunProject.Currencies;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class CurrencyTest
{
  [TestCase]
  public static void StartingBalancesAreIndependentForEachCampaignAndCurrencyReference()
  {
    var currency = MakeCurrency();
    var sameName = MakeCurrency();
    var start = MakeStart(startingCurrencies: new() { [currency] = 10 });
    using var first = new GeoscapeFixture(start);
    using var second = new GeoscapeFixture(start);
    start.StartingCurrencies[currency] = 99;
    Assert.True(first.State.TrySpendCurrencyCosts(new() { [currency] = 3 }));
    Assert.Equal(7UL, first.State.GetCurrencyBalance(currency));
    Assert.Equal(10UL, second.State.GetCurrencyBalance(currency));
    Assert.Equal(0UL, first.State.GetCurrencyBalance(sameName));
    Assert.Equal(99UL, start.StartingCurrencies[currency]);
  }

  [TestCase]
  public static void MultiCurrencySpendIsAtomic()
  {
    var a = MakeCurrency("A");
    var b = MakeCurrency("B");
    using var campaign = new GeoscapeFixture(MakeStart(startingCurrencies: new() { [a] = 10, [b] = 1 }));
    var costs = new Godot.Collections.Dictionary<Currency, ulong> { [a] = 7, [b] = 2 };
    Assert.False(campaign.State.CanAffordCurrencyCosts(costs));
    Assert.False(campaign.State.TrySpendCurrencyCosts(costs));
    Assert.Equal(10UL, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(1UL, campaign.State.GetCurrencyBalance(b));
    costs[b] = 1;
    Assert.True(campaign.State.TrySpendCurrencyCosts(costs));
    Assert.Equal(3UL, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(0UL, campaign.State.GetCurrencyBalance(b));
  }

  [TestCase]
  public static void FreeAndMaximumUnsignedCostsDoNotWrap()
  {
    var currency = MakeCurrency();
    var missing = MakeCurrency();
    using var campaign = new GeoscapeFixture(MakeStart(startingCurrencies: new() { [currency] = ulong.MaxValue }));
    Assert.True(campaign.State.TrySpendCurrencyCosts(new()));
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [missing] = 0 }));
    Assert.Equal(0UL, campaign.State.GetCurrencyBalance(missing));
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [currency] = ulong.MaxValue }));
    Assert.Equal(0UL, campaign.State.GetCurrencyBalance(currency));
    Assert.False(campaign.State.TrySpendCurrencyCosts(new() { [currency] = 1 }));
  }
}
