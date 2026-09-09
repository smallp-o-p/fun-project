using System.Collections.Generic;
using Godot;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>At least one child must pass; an empty list is false. Short-circuits on the first success.</summary>
[GlobalClass]
public partial class AnyResearchConditions : ResearchCondition
{
  [Export] public ResearchCondition[] Children { get; set; } = [];

  internal override bool IsMet(in CampaignGameState state)
  {
    foreach (var child in Children)
      if (child.IsMet(state))
        return true;
    return false;
  }

  internal override void CheckContents(SysColGeneric.HashSet<ResearchCondition> path)
    => CheckChildren(Children, path);
}
