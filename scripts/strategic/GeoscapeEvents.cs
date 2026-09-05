using System;
using FunProject.Engineering;

namespace FunProject.Strategic;

public interface IGeoscapeEvent;

public sealed record GeoscapeEvent(
  GeoscapeEventDefinition Definition,
  Option<int> TargetRegionIndex,
  long OccurredTick,
  Option<long> ExpiresAtTick);

public sealed record ManufacturingStarted(ManufacturingJob Job) : IGeoscapeEvent;

public sealed record ManufacturingCompleted(ManufacturingJob Job) : IGeoscapeEvent;

public sealed record TimeAdvanced(long Tick, DateTime CurrentTime) : IGeoscapeEvent;

public sealed record ScheduledEventFired(GeoscapeEvent Event) : IGeoscapeEvent;

public sealed record EventExpired(GeoscapeEvent Event) : IGeoscapeEvent;

public sealed record ResolutionEventOpened(PendingResolution Pending) : IGeoscapeEvent;

public sealed record ResolutionEventClosed(PendingResolution Resolved, ResolutionOutcome SelectedOutcome) : IGeoscapeEvent;
