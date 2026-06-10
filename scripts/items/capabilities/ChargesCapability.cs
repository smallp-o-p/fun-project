using System;

namespace FunProject.Items.Capabilities;

public class ChargesCapability : ItemCapability
{
  public int MaxCharges { get; }
  public int Current { get; private set; }
  public bool IsDepleted => Current <= 0;

  public ChargesCapability(ChargesCapabilityData data)
  {
    MaxCharges = Math.Max(0, data.MaxCharges);
    Current = MaxCharges;
  }

  public bool TrySpend(int amount = 1)
  {
    if (amount < 0 || Current < amount)
      return false;

    Current -= amount;
    return true;
  }

  public void Restore() => Current = MaxCharges;
}
