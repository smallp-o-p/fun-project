using FunProject.Core;
using FunProject.Currencies;
using FunProject.Engineering;
using Godot;

namespace FunProject.Research;

/// <summary>
/// Authored research project: completion duration in whole days, the condition gating
/// its start, and the manufacturing recipes unlocked on completion. Identity is resource
/// identity; different resources with the same name remain different projects.
/// </summary>
[GlobalClass]
public partial class ResearchProject : NamedEntityData
{
  [Export] public uint DurationDays { get; set; } = 1;

  [Export] public required ResearchCondition Condition { get; set; }

  [Export] public ManufacturingRecipe[] ManufacturingUnlocks { get; set; } = [];

  [Export] public Godot.Collections.Dictionary<Currency, ulong> Costs { get; set; } = [];
}
