using System;

namespace FunProject.Items.Capabilities;

public class ChargesCapability : ItemCapability
{
  public int MaxCharges { get; }
  private int _current;
  public int Current
  {
    get { _battleAccess.RequireRead(); return _current; }
    private set { _battleAccess.RequireMutation(); _current = value; }
  }
  public bool IsDepleted => Current <= 0;

  public ChargesCapability(ChargesCapabilityData data)
  {
    MaxCharges = Math.Max(0, data.MaxCharges);
    Current = MaxCharges;
  }

  public bool TrySpend(int amount = 1)
  {
    _battleAccess.RequireMutation();
    if (amount < 0 || Current < amount)
      return false;

    Current -= amount;
    return true;
  }

  public void Restore() { _battleAccess.RequireMutation(); Current = MaxCharges; }
}
