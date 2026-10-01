using System;
using System.Collections.Generic;
using FunProject.Engineering;
using FunProject.Items;
using FunProject.Research;
using FunProject.Combatants;

namespace FunProject.Strategic;

public interface IGeoscapeEvent;

public sealed record GeoscapeEvent(
  GeoscapeEventDefinition Definition,
  Option<int> TargetRegionIndex,
  long OccurredTick,
  Option<long> ExpiresAtTick)
{
  /// <summary>Seed minted once when a tactical event fires and retained on the active record,
  /// so battle resolution reproduces the same force across session rebuilds. Non-tactical
  /// events carry None.</summary>
  public Option<int> BattleSeed { get; init; } = Option<int>.None;
}

public sealed record ManufacturingStarted(ManufacturingJob Job) : IGeoscapeEvent;

public sealed record ManufacturingCompleted(ManufacturingJob Job) : IGeoscapeEvent;

public sealed record ResearchStarted(ResearchJob Job) : IGeoscapeEvent;

public sealed record ResearchCompleted(ResearchJob Job,
  IReadOnlyList<EquippableItemData> ManufacturingUnlocks) : IGeoscapeEvent;

public sealed record TimeAdvanced(long Tick, DateTime CurrentTime) : IGeoscapeEvent;

public sealed record ScheduledEventFired(GeoscapeEvent Event) : IGeoscapeEvent;

public sealed record EventExpired(GeoscapeEvent Event) : IGeoscapeEvent;

public sealed record ResolutionEventOpened(PendingResolution Pending) : IGeoscapeEvent;

public sealed record ResolutionEventClosed(PendingResolution Resolved, ResolutionOutcome SelectedOutcome) : IGeoscapeEvent;

public sealed record CombatantConditionsChanged(Combatant Combatant) : IGeoscapeEvent;
