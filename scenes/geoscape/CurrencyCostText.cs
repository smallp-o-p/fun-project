using System.Collections.Generic;
using FunProject.Currencies;

public static class CurrencyCostText
{
  public static string Format(IReadOnlyDictionary<Currency, long> costs, IReadOnlyList<CurrencyShortfall> shortfalls)
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
