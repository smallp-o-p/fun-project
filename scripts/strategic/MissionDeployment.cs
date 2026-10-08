using FunProject.Combatants;
using System.Collections.Generic;

namespace FunProject.Strategic;

/// <summary>Launch-time squad identities; startup callbacks cannot change this snapshot.
/// Equipment and conditions remain live on the original combatant references.</summary>
public sealed record MissionDeployment(IReadOnlyList<Combatant> Participants);
