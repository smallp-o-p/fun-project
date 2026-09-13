using FunProject.Combatants;
using Godot;

namespace FunProject.GameState;

/// <summary>
/// One starting-captive stamp: the unit mold and its original faction, plus an optional
/// individual display name. Empty <see cref="DisplayName"/> falls back to the mold's name.
/// </summary>
[GlobalClass]
public partial class CaptiveEntryData : Resource
{
  [Export] public required CombatantData Unit { get; set; }

  [Export] public required FactionData Faction { get; set; }

  [Export] public string DisplayName { get; set; } = "";
}
