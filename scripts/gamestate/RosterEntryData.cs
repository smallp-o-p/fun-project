using FunProject.Combatants;
using Godot;

namespace FunProject.GameState;

/// <summary>
/// One starting-roster stamp: the unit mold plus an optional individual display name.
/// Empty <see cref="DisplayName"/> falls back to the mold's name (hero entries rely on that).
/// </summary>
[GlobalClass]
public partial class RosterEntryData : Resource
{
  [Export] public required CombatantData Unit { get; set; }

  [Export] public string DisplayName { get; set; } = "";
}
