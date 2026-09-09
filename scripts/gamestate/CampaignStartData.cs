using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Research;
using FunProject.Stats;
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

  // Which item/mod types the campaign starts stocked with. Stock policy (unlimited or
  // scarce) is authored on each template's UnlimitedStock flag; quantities are runtime-only.
  [Export] public EquippableItemData[] Armory { get; set; } = [];

  [Export] public EquippableMod[] ModStock { get; set; } = [];

  [Export] public EquippableItemData[] ManufacturableItems { get; set; } = [];

  [Export] public ResearchProject[] ResearchProjects { get; set; } = [];
}
