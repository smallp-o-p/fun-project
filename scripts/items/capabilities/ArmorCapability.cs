using System;
using FunProject.Core;

namespace FunProject.Items.Capabilities;

public class ArmorCapability : ItemCapability
{
  public int Max { get; }
  private int _current;
  public int Current
  {
    get { _battleAccess.RequireRead(); return _current; }
    private set { _battleAccess.RequireMutation(); _current = value; }
  }
  public Element Element { get; }
  public int RegenDelayTurns { get; }
  public int RegenPerTurn { get; }
  private int _regenDelayRemaining;
  public int RegenDelayRemaining
  {
    get { _battleAccess.RequireRead(); return _regenDelayRemaining; }
    private set { _battleAccess.RequireMutation(); _regenDelayRemaining = value; }
  }

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
    _battleAccess.RequireMutation();
    ArgumentOutOfRangeException.ThrowIfLessThan(amount, 0);
    Current = Math.Max(Current - amount, 0);
  }

  public void RearmRegenDelay()
  {
    _battleAccess.RequireMutation();
    RegenDelayRemaining = RegenDelayTurns;
  }

  public int TickRegen()
  {
    _battleAccess.RequireMutation();
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
