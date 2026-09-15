using FunProject.Dialogue;
using Godot;

namespace FunProject.Strategic;

public enum GeoscapeEventKind
{
  TacticalBattle,
  Plot,
  Minigame,
}

[GlobalClass]
public partial class GeoscapeEventDefinition : Resource
{
  [Export] public GeoscapeEventKind Kind { get; set; } = GeoscapeEventKind.Plot;

  [Export] public string Title { get; set; } = "Untitled event";

  [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

  // -1 = never expires (Godot cannot export Nullable<int>).
  [Export] public int ExpiresAfterTicks { get; set; } = -1;

  // Region name matching a RegionData.Name in GeoscapeMapData.Regions; empty = map-wide.
  // Resolved to an index when the session is constructed — unknown names throw at load.
  [Export] public string TargetRegionName { get; set; } = "";

  // Deployment eligibility override: unfit combatants (injured or exhausted) may join this
  // mission anyway. Default false — the roster gate stands.
  [Export] public bool AllowUnfitDeployment { get; set; } = false;
  
  // If dialogue needs to be triggered
  [Export] public DialogueSequenceData? Dialogue { get; set; }
}
