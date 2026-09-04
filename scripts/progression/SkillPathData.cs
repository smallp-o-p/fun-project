using FunProject.Core;
using Godot;

namespace FunProject.Progression;

/// <summary>
/// One authored progression path: a linear chain of upgrade steps, unlocked strictly in order.
/// </summary>
[GlobalClass]
public partial class SkillPathData : NamedEntityData
{
  [Export] public Godot.Collections.Array<SkillUpgradeStepData> Steps { get; set; } = [];
}
