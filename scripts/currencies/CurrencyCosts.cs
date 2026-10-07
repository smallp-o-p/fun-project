using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FunProject.Currencies;

internal static class CurrencyCosts
{
  internal static IReadOnlyDictionary<Currency, long> Capture(Godot.Collections.Dictionary<Currency, long> authored)
  {
    ArgumentNullException.ThrowIfNull(authored);
    var captured = new Dictionary<Currency, long>(ReferenceEqualityComparer.Instance);
    foreach (var (currency, amount) in authored)
    {
      CheckAmount(currency, amount);
      captured.Add(currency, amount);
    }
    return new ReadOnlyDictionary<Currency, long>(captured);
  }

  internal static IReadOnlyList<CurrencyShortfall> Shortfalls(
    IReadOnlyDictionary<Currency, long> costs, IReadOnlyDictionary<Currency, long> balances)
  {
    ArgumentNullException.ThrowIfNull(costs);
    ArgumentNullException.ThrowIfNull(balances);
    var shortfalls = new List<CurrencyShortfall>();
    foreach (var (currency, required) in costs)
    {
      CheckAmount(currency, required);
      long available = balances.GetValueOrDefault(currency);
      if (required > available)
        shortfalls.Add(new CurrencyShortfall(currency, required, available));
    }
    return shortfalls.AsReadOnly();
  }

  private static void CheckAmount(Currency currency, long amount)
  {
    ArgumentNullException.ThrowIfNull(currency);
    ArgumentOutOfRangeException.ThrowIfNegative(amount);
  }
}
