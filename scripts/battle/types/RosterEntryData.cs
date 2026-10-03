using Godot;

namespace FunProject.Battle;

/// <summary>Authored roster entry: a loadout template expanded Quantity times per battle.</summary>
[GlobalClass]
public partial class RosterEntryData : Resource
{
  [Export] public required UnitLoadoutData Loadout { get; set; }

  [Export] public int Quantity { get; set; } = 1;
}
