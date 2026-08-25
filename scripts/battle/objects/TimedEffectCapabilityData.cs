using FunProject.Items.Effects;
using Godot;

namespace FunProject.Battle;

/// <summary>
/// A capability where a BattleEffectData is pushed after `FireAfterTurns` turns in a radius of `EffectRadius`.
/// </summary>
[GlobalClass]
public sealed partial class TimedEffectCapabilityData : SpecialObjectCapabilityData
{
  [Export] public int FireAfterTurns { get; set; } = 5;

  [Export] public int EffectRadius { get; set; } = 1;

  [Export] public Godot.Collections.Array<BattleEffectData> Effects { get; set; } = [];

  public override SpecialObjectCapability CreateRuntime() => new TimedEffectCapability(this);
}

/// <summary>
/// Runtime payload of TimedEffectCapability
/// </summary>
public sealed class TimedEffectCapability(TimedEffectCapabilityData data) : SpecialObjectCapability
{
  public int ExpireAfterTurns { get; } = data.FireAfterTurns;
  public int EffectRadius { get; } = System.Math.Max(0, data.EffectRadius);
  public SysColGeneric.IReadOnlyList<BattleEffectData> Effects { get; } = [.. data.Effects];
}
