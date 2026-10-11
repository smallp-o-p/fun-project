using FunProject.Currencies;
using Godot;

namespace FunProject.Staffing;

[GlobalClass]
public partial class StaffingRulesData : Resource
{
  // Additive speed multiplier per employee: 0.001 means +0.1%.
  [Export] public float ResearcherSpeedBonus { get; set; } = 0.001f;
  [Export] public float EngineerSpeedBonus { get; set; } = 0.001f;
  [Export] public Godot.Collections.Dictionary<Currency, ulong> ResearcherCosts { get; set; } = [];
  [Export] public Godot.Collections.Dictionary<Currency, ulong> EngineerCosts { get; set; } = [];
}
