using FunProject.Combatants;
using FunProject.Items;
using Godot;

namespace FunProject.Battle;

/// <summary>Authored faction roster/objective bundle inside a battle type.</summary>
[GlobalClass]
public partial class FactionDeploymentData : Resource
{
  [Export] public required FactionData Faction { get; set; }

  [Export] public Godot.Collections.Array<RosterEntryData> Roster { get; set; } = [];

  [Export] public Godot.Collections.Array<ObjectiveData> Objectives { get; set; } = [];
}
