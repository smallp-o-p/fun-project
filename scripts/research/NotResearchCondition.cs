using System.Collections.Generic;
using Godot;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>Negates its single required child.</summary>
[GlobalClass]
public partial class NotResearchCondition : ResearchCondition
{
  [Export] public required ResearchCondition Child { get; set; }

  internal override bool IsMet(in CampaignGameState state) => !Child.IsMet(state);

  internal override void CheckContents(SysColGeneric.HashSet<ResearchCondition> path)
    => CheckChild(Child, path);
}
