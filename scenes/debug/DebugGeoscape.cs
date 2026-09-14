using FunProject.GameState;
using CampaignGameState = global::FunProject.GameState.GameState;

// Debug bring-up: playtest host for the injury/fatigue flow. Adds two soldiers stamped from
// the authored template on top of the campaign's roster, then reports each as returning
// from a mission with 10% and 60% health damage — the default injury tiers 1 (Lightly) and
// 3 (Gravely) plus the normal first-return fatigue. State initialization runs once per new
// campaign, so leaving and re-entering the geoscape never re-applies or resets recovery.
public sealed partial class DebugGeoscape : GeoscapeScene
{
  protected override CampaignGameState CreateGameState(CampaignStartData start)
  {
    var debugStart = (CampaignStartData)start.Duplicate();
    int firstExtra = start.StartingRoster.Length;
    var template = start.StartingRoster[0].Unit;
    debugStart.StartingRoster =
    [
      .. start.StartingRoster,
      new RosterEntryData { Unit = template, DisplayName = "Pvt. Ellis Ward" },
      new RosterEntryData { Unit = template, DisplayName = "Cpl. Noor Reyes" },
    ];

    CampaignGameState state = base.CreateGameState(debugStart);
    state.Conditions.ApplyMissionReturn(state.Roster[firstExtra], 10, 100, state.Tick);
    state.Conditions.ApplyMissionReturn(state.Roster[firstExtra + 1], 60, 100, state.Tick);
    return state;
  }
}
