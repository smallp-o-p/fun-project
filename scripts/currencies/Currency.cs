using FunProject.Core;
using Godot;

namespace FunProject.Currencies;

/// <summary>A campaign currency identified by its shared resource reference.</summary>
[GlobalClass]
public partial class Currency : NamedEntityData
{
  public static string FormatCosts(Godot.Collections.Dictionary<Currency, ulong> costs)
  {
    var amounts = costs.AsValueEnumerable().Where(pair => pair.Value > 0)
      .Select(pair => $"{pair.Key.Name}: {pair.Value}").ToArray();
    return amounts.Length == 0 ? "Cost: Free" : "Cost: " + string.Join(", ", amounts);
  }
}
