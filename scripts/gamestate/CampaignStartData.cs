using FunProject.Combatants;
using FunProject.Core;
using FunProject.Strategic;
using Godot;

namespace FunProject.GameState;

/// <summary>
/// Authored campaign definition: the map, the player's faction, and the starting roster.
/// The single resource a new campaign is built from.
/// </summary>
[GlobalClass]
public partial class CampaignStartData : NamedEntityData
{
  [Export] public required GeoscapeMapData Map { get; set; }

  [Export] public required FactionData PlayerFaction { get; set; }

  [Export] public RosterEntryData[] StartingRoster { get; set; } = [];

  [Export] public ArmoryEntryData[] Armory { get; set; } = [];

  [Export] public ModStockEntryData[] ModStock { get; set; } = [];
}
