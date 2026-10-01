using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.GameState;

// Test-hosted geoscape root: captures the campaign its base builds so handoff tests can
// prove one campaign/root instance survives battle presentation and return. Used through
// the serialized test scene that overrides the authored root's script.
internal sealed partial class GeoscapeBattleHandoffScene : GeoscapeScene
{
  public CampaignGameState? Campaign { get; private set; }

  protected override CampaignGameState CreateGameState(CampaignStartData start)
  {
    Campaign = base.CreateGameState(start);
    return Campaign;
  }
}
