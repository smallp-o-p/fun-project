using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Strategic;

/// <summary>Campaign-held identity and captured squad for one live mission. The pending
/// resolution retains the event; the presentation handle owns the runtime.</summary>
public sealed class MissionDeployment
{
  internal MissionDeployment(IReadOnlyList<Combatant> participants)
  {
    ArgumentNullException.ThrowIfNull(participants);

    Participants = [.. participants];
  }

  /// <summary>A private copy taken at launch; later caller mutations of the selection cannot
  /// reach it.</summary>
  public IReadOnlyList<Combatant> Participants { get; }
}
