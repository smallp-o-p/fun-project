using FunProject.Currencies;
using Godot;

namespace FunProject.Staffing;

[GlobalClass]
public partial class StaffingRulesData : Resource
{
  [Export] public uint ResearcherBonusUnits { get; set; } = 1000;
  [Export] public uint EngineerBonusUnits { get; set; } = 1000;
  [Export] public Godot.Collections.Dictionary<Currency, ulong> ResearcherCosts { get; set; } = [];
  [Export] public Godot.Collections.Dictionary<Currency, ulong> EngineerCosts { get; set; } = [];
}
