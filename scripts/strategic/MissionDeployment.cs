using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Strategic;

/// <summary>Campaign-side identity of one launched mission: the exact pending event it was
/// launched from (compared by reference identity) and the squad captured at launch. The
/// association itself is campaign truth on <c>GameState.ActiveMission</c>; this object only
/// identifies the deployment that handle and return paths validate against.</summary>
public sealed class MissionDeployment
{
  internal MissionDeployment(GeoscapeEvent mission, IReadOnlyList<Combatant> participants)
  {
    ArgumentNullException.ThrowIfNull(mission);
    ArgumentNullException.ThrowIfNull(participants);

    Event = mission;
    Participants = [.. participants];
  }

  public GeoscapeEvent Event { get; }

  /// <summary>A private copy taken at launch; later caller mutations of the selection cannot
  /// reach it.</summary>
  public IReadOnlyList<Combatant> Participants { get; }
}
