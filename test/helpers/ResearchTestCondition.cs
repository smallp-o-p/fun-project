using System;
using FunProject.Research;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Tests;

// Proves the condition model is extensible without registration: an arbitrary predicate
// over the supplied campaign state, defined entirely in test code.
public partial class ResearchTestCondition : ResearchCondition
{
  internal Func<CampaignGameState, bool> Predicate { get; set; } = _ => true;

  internal override bool IsMet(in CampaignGameState state) => Predicate(state);
}
