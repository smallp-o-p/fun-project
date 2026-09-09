using Godot;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>Always true: makes initially available projects explicit.</summary>
[GlobalClass]
public partial class AlwaysResearchCondition : ResearchCondition
{
  internal override bool IsMet(in CampaignGameState state) => true;
}
