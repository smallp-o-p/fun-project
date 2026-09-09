using System.Collections.Generic;
using Godot;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>All children must pass; an empty list is true. Short-circuits on the first failure.</summary>
[GlobalClass]
public partial class AllResearchConditions : ResearchCondition
{
  [Export] public ResearchCondition[] Children { get; set; } = [];

  internal override bool IsMet(in CampaignGameState state)
  {
    foreach (var child in Children)
      if (!child.IsMet(state))
        return false;
    return true;
  }

  internal override void CheckContents(SysColGeneric.HashSet<ResearchCondition> path)
    => CheckChildren(Children, path);
}
