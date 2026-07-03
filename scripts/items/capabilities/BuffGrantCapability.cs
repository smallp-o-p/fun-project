using System;
using System.Collections.Generic;
using FunProject.Buffs;

namespace FunProject.Items.Capabilities;

/// <summary>Grants the carried buffs to the unit that has this item equipped.</summary>
public sealed class BuffGrantCapability : ItemCapability
{
  public IReadOnlyList<BuffData> Buffs { get; }

  public BuffGrantCapability(BuffGrantCapabilityData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    Buffs = [.. data.Buffs];
  }
}
