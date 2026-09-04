using Godot;

namespace FunProject.Buffs;

/// <summary>
/// Authored activation condition for a buff. Dumb data: identity is the concrete subclass
/// (no enums); evaluation lives battle-side in BuffCondition, mapped by BuffCondition.Create.
/// One concrete condition per file: .tres references C# scripts by path, so a second
/// [GlobalClass] in this file would be unaddressable from authored resources.
/// </summary>
[GlobalClass]
public abstract partial class BuffConditionData : Resource
{
}
