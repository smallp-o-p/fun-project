using System;
using System.Collections.Generic;
using Godot;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>True once the referenced project has completed in this campaign.</summary>
[GlobalClass]
public partial class ResearchCompletedCondition : ResearchCondition
{
  [Export] public required ResearchProject Project { get; set; }

  internal override bool IsMet(in CampaignGameState state) => state.Research.IsCompleted(Project);

  internal override void CheckContents(SysColGeneric.HashSet<ResearchCondition> path)
    => ArgumentNullException.ThrowIfNull(Project);
}
