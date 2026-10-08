using System;
using FunProject.Currencies;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class CurrencyTest
{
  [TestCase]
  public void CapturedStartingBalancesAreIsolatedAndReadOnly()
  {
    var currency = MakeCurrency();
    var sameName = MakeCurrency();
    var start = MakeStart(startingCurrencies: new() { [currency] = 10 });
    using var first = new GeoscapeFixture(start);
    using var second = new GeoscapeFixture(start);
    start.StartingCurrencies[currency] = 99;
    var snapshot = first.State.Currencies;
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IDictionary<Currency, long>)snapshot)[currency] = 8);
    Assert.True(first.State.TrySpendCurrencyCosts(Currency.CaptureCosts(new() { [currency] = 3 })));
    Assert.Equal(7L, first.State.GetCurrencyBalance(currency));
    Assert.Equal(10L, second.State.GetCurrencyBalance(currency));
    Assert.Equal(10L, snapshot[currency]);
    Assert.Equal(0L, first.State.GetCurrencyBalance(sameName));
  }

  [TestCase]
  public void MalformedInitialAmountsFailConstruction()
  {
    var currency = MakeCurrency();
    var missing = MakeStart();
    missing.StartingCurrencies = null!;
    Assert.Throws<ArgumentNullException>(() => new FunProject.GameState.GameState(missing));
    var negative = MakeStart(startingCurrencies: new() { [currency] = -1 });
    Assert.Throws<ArgumentOutOfRangeException>(() => new FunProject.GameState.GameState(negative));
    Assert.Throws<ArgumentNullException>(() => Currency.CaptureCosts(new() { [null!] = 1 }));
  }

  [TestCase]
  public void MultiCurrencySpendIsAtomic()
  {
    var a = MakeCurrency("A");
    var b = MakeCurrency("B");
    using var campaign = new GeoscapeFixture(MakeStart(startingCurrencies: new() { [a] = 10, [b] = 1 }));
    var authored = new Godot.Collections.Dictionary<Currency, long> { [a] = 7, [b] = 2 };
    var costs = Currency.CaptureCosts(authored);
    authored[b] = 0;
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IDictionary<Currency, long>)costs).Clear());
    var shortfalls = campaign.State.GetCurrencyShortfalls(costs);
    Assert.Equal(1, shortfalls.Count);
    Assert.Equal((b, 2L, 1L), shortfalls[0]);
    Assert.Throws<NotSupportedException>(() => ((SysColGeneric.IList<(Currency Currency, long Required, long Available)>)shortfalls).Clear());
    Assert.False(campaign.State.TrySpendCurrencyCosts(costs));
    Assert.Equal(10L, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(1L, campaign.State.GetCurrencyBalance(b));
    Assert.True(campaign.State.TrySpendCurrencyCosts(Currency.CaptureCosts(new() { [a] = 7, [b] = 1 })));
    Assert.Equal(3L, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(0L, campaign.State.GetCurrencyBalance(b));
    Assert.Equal((b, 2L, 1L), shortfalls[0]);
  }

  [TestCase]
  public void FreeCostsAndMaximumAmountsDoNotWrapOrAddMissingKeys()
  {
    var currency = MakeCurrency();
    var missing = MakeCurrency();
    using var campaign = new GeoscapeFixture(MakeStart(startingCurrencies: new() { [currency] = long.MaxValue }));
    Assert.True(campaign.State.TrySpendCurrencyCosts(Currency.CaptureCosts(new())));
    Assert.True(campaign.State.TrySpendCurrencyCosts(Currency.CaptureCosts(new() { [missing] = 0 })));
    Assert.Equal(0, campaign.State.GetCurrencyShortfalls(Currency.CaptureCosts(new() { [missing] = 0 })).Count);
    Assert.Equal(1, campaign.State.Currencies.Count);
    Assert.True(campaign.State.TrySpendCurrencyCosts(Currency.CaptureCosts(new() { [currency] = long.MaxValue })));
    Assert.Equal(0L, campaign.State.GetCurrencyBalance(currency));
    Assert.Throws<ArgumentNullException>(() => campaign.State.GetCurrencyBalance(null!));
    Assert.Throws<ArgumentNullException>(() => campaign.State.TrySpendCurrencyCosts(null!));
    Assert.Throws<ArgumentOutOfRangeException>(() => campaign.State.TrySpendCurrencyCosts(
      new SysColGeneric.Dictionary<Currency, long> { [currency] = 0, [missing] = -1 }));
    Assert.Equal(1, campaign.State.Currencies.Count);
    Assert.Equal(0L, campaign.State.GetCurrencyBalance(currency));
  }
}
