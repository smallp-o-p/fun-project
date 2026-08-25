using Godot;

namespace FunProject.Battle;

/// <summary>Authored capability allowing an adjacent unit to interact with an object.</summary>
[GlobalClass]
public sealed partial class InteractiveCapabilityData : SpecialObjectCapabilityData
{
  [Export] public int ActionPointCost { get; set; } = 1;

  public override SpecialObjectCapability CreateRuntime() => new InteractiveCapability(this);
}

/// <summary>Runtime interaction capability with a fixed action-point cost.</summary>
public sealed class InteractiveCapability(InteractiveCapabilityData data) : SpecialObjectCapability
{
  public int ActionPointCost { get; } = data.ActionPointCost;
}
