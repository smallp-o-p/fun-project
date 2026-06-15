using FunProject.Core;
using Godot;

namespace FunProject.Battle;

// Authored objective template. Config only; CreateRuntime() builds the runtime
// instance that owns behavior and per-instance state (mirrors ItemCapabilityData).
[GlobalClass]
public abstract partial class ObjectiveData : NamedEntityData
{
  public abstract Objective CreateRuntime();
}
