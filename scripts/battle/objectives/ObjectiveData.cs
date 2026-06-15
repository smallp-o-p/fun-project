using FunProject.Core;
using Godot;

namespace FunProject.Battle;

// Authored display data for an objective: name + description only (inherited).
// Objective behavior and parameters live in code (the runtime Objective subclasses);
// this resource carries just the designer-facing text. Not subclassed per kind.
[GlobalClass]
public sealed partial class ObjectiveData : NamedEntityData
{
}
