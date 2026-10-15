using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Strategic;

/// <summary>Launch-time squad identities; startup callbacks cannot change this snapshot.
/// Equipment and conditions remain live on the original combatant references.</summary>
public sealed record MissionDeployment(GeoscapeEvent Mission, IReadOnlyList<Combatant> Participants,
  MissionTerms Terms)
{
  internal bool Installed { get; set; }
  public Option<MissionReceipt> Receipt { get; internal set; }
}
