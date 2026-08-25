using Godot;

namespace FunProject.Battle;

/// <summary>Authored capability resource that produces one runtime object capability.</summary>
[GlobalClass]
public abstract partial class SpecialObjectCapabilityData : Resource
{
  public abstract SpecialObjectCapability CreateRuntime();
}

/// <summary>Runtime marker base for board-object capabilities.</summary>
public abstract class SpecialObjectCapability;
