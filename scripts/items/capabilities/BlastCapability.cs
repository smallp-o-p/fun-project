using System;
using FunProject.Items.Effects;

namespace FunProject.Items.Capabilities;

public class BlastCapability(BlastCapabilityData data) : ItemCapability
{
  public int BlastRadius { get; } = Math.Max(0, data.BlastRadius);
  public SysColGeneric.IReadOnlyList<BattleEffectData> Effects { get; } = [.. data.Effects];
}
