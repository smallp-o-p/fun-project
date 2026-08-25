using Godot;

namespace FunProject.Battle;

/// <summary>
/// Authored declaration for an extra runtime system a battle type should register. Subclasses
/// register through the runtime's standard generic hook door — the event key is statically
/// known in the subclass, so no Type-based registration surface is needed.
/// </summary>
[GlobalClass]
public abstract partial class BattleTypeSystemData : Resource
{
  public abstract void Register(BattleRuntime runtime);
}
