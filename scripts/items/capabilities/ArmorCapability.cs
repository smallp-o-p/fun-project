using System;
using FunProject.Core;

namespace FunProject.Items.Capabilities;

public class ArmorCapability : ItemCapability
{
  public int Max { get; }
  public int Current { get; private set; }
  public Element Element { get; }
  public int RegenDelayTurns { get; }
  public int RegenPerTurn { get; }
  public int RegenDelayRemaining { get; private set; }

  public bool CanRegen => RegenPerTurn > 0;
  public bool IsDepleted => Current <= 0;

  public ArmorCapability(ArmorCapabilityData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (data.ArmorStat is null)
      throw new InvalidOperationException("Armor capability requires an ArmorStat.");

    Max = Math.Max(0, data.ArmorStat.BaseValue);
    Current = Max;
    Element = data.ArmorStat.Element;
    RegenDelayTurns = Math.Max(0, data.RegenDelayTurns);
    RegenPerTurn = Math.Max(0, data.RegenPerTurn);
  }

  public void Reduce(int amount)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(amount, 0);
    Current = Math.Max(Current - amount, 0);
  }

  public void RearmRegenDelay()
  {
    RegenDelayRemaining = RegenDelayTurns;
  }

  public int TickRegen()
  {
    if (RegenDelayRemaining > 0)
    {
      RegenDelayRemaining--;
      return 0;
    }

    int restored = Math.Min(RegenPerTurn, Max - Current);
    Current += restored;
    return restored;
  }
}
