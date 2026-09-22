using FunProject.Stats;
using Godot;
using System;

namespace FunProject.Battle;

/// <summary>Authored health for a special board object; placement materializes one
/// independent runtime health per placed instance.</summary>
[GlobalClass]
public sealed partial class ObjectHealthCapabilityData : SpecialObjectCapabilityData
{
  [Export] public required HealthStat HealthStat { get; set; }
  public override SpecialObjectCapability CreateRuntime() => new ObjectHealthCapability(this);
}

/// <summary>Runtime health of one board object. Positive authored health and the terminal
/// transition at zero keep every live health-bearing object's health positive.</summary>
public sealed class ObjectHealthCapability : SpecialObjectCapability
{
  public int MaxHealth { get; }
  public int CurrentHealth { get; private set; }

  public ObjectHealthCapability(ObjectHealthCapabilityData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (data.HealthStat is null)
      throw new InvalidOperationException("Object health capability requires a HealthStat.");
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(data.HealthStat.BaseValue);
    MaxHealth = data.HealthStat.BaseValue;
    CurrentHealth = MaxHealth;
  }

  internal void Reduce(int amount)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(amount);
    CurrentHealth = Math.Max(0, CurrentHealth - amount);
  }
}
