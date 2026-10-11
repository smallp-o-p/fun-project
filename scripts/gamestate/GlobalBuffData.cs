using FunProject.Core;
using Godot;

namespace FunProject.GameState;

public enum GlobalProcess { HiringCost, ResearchSpeed, ManufacturingSpeed }

/// <summary>Shared authored definition; campaign membership belongs to GameState.</summary>
[GlobalClass]
public partial class GlobalBuffData : NamedEntityData
{
  [Export] public GlobalProcess Process { get; set; }
  // Signed additive percentage: 0.2 means +20%; -0.2 means -20%.
  [Export] public double Modifier { get; set; }
}
