using System;

namespace FunProject.Items.Capabilities;

public class ThrowableCapability(ThrowableCapabilityData data) : ItemCapability
{
  public int ThrowRange { get; } = Math.Max(0, data.ThrowRange);
  public int ActionPointCost { get; } = Math.Max(0, data.ActionPointCost);
  public bool ConsumesOnUse { get; } = data.ConsumesOnUse;
}
