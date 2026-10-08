using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FunProject.Core;
using Godot;

namespace FunProject.Currencies;

/// <summary>A campaign currency identified by its shared resource reference.</summary>
[GlobalClass]
public partial class Currency : NamedEntityData
{
  internal static IReadOnlyDictionary<Currency, long> CaptureCosts(Godot.Collections.Dictionary<Currency, long> authored)
  {
    ArgumentNullException.ThrowIfNull(authored);
    var captured = new Dictionary<Currency, long>(ReferenceEqualityComparer.Instance);
    foreach (var (currency, amount) in authored)
    {
      ValidateAmount(currency, amount);
      captured.Add(currency, amount);
    }
    return new ReadOnlyDictionary<Currency, long>(captured);
  }

  internal static void ValidateAmount(Currency currency, long amount)
  {
    ArgumentNullException.ThrowIfNull(currency);
    ArgumentOutOfRangeException.ThrowIfNegative(amount);
  }

  public static string FormatCosts(IReadOnlyDictionary<Currency, long> costs,
    IReadOnlyList<(Currency Currency, long Required, long Available)> shortfalls)
  {
    var amounts = new List<string>();
    foreach (var (currency, required) in costs)
      if (required > 0)
        amounts.Add($"{currency.Name}: {required}");
    if (amounts.Count == 0)
      return "Cost: Free";

    string text = "Cost: " + string.Join(", ", amounts);
    if (shortfalls.Count > 0)
    {
      var missing = new List<string>();
      foreach (var shortfall in shortfalls)
        missing.Add($"{shortfall.Currency.Name}: required {shortfall.Required}, available {shortfall.Available}, missing {checked(shortfall.Required - shortfall.Available)}");
      text += "\nShortfall: " + string.Join("; ", missing);
    }
    return text;
  }
}
