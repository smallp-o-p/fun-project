using FunProject.Battle;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

// Subscribes to a session's (or runtime's) committed event stream on construction and records
// every event, replacing the ubiquitous `var events = new List<BattleEvent>(); ... += events.Add`.
internal sealed class BattleEventRecorder
{
  private readonly List<BattleEvent> _events = [];

  public BattleEventRecorder(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    session.BattleEventCommitted += _events.Add;
  }

  public BattleEventRecorder(BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    runtime.BattleEventCommitted += _events.Add;
  }

  public IReadOnlyList<BattleEvent> All => _events;

  public IEnumerable<TEvent> OfType<TEvent>() where TEvent : BattleEvent => _events.AsValueEnumerable().OfType<TEvent>().ToArray();

  public TEvent Single<TEvent>() where TEvent : BattleEvent
  {
    List<TEvent> matches = _events.AsValueEnumerable().OfType<TEvent>().ToList();
    if (matches.Count != 1)
      throw new InvalidOperationException(
        $"Expected exactly one {typeof(TEvent).Name} but found {matches.Count}.");

    return matches[0];
  }

  // Asserts a TEarlier event was committed and that a TLater event followed it.
  public void AssertCommittedBefore<TEarlier, TLater>()
    where TEarlier : BattleEvent
    where TLater : BattleEvent
  {
    int earlierIndex = IndexOf<TEarlier>();
    int laterIndex = IndexOf<TLater>();
    Assert.True(earlierIndex >= 0, $"Expected a {typeof(TEarlier).Name} to have been committed.");
    Assert.True(laterIndex >= 0, $"Expected a {typeof(TLater).Name} to have been committed.");
    Assert.True(
      laterIndex > earlierIndex,
      $"Expected {typeof(TLater).Name} to be committed after {typeof(TEarlier).Name}.");
  }

  private int IndexOf<TEvent>() where TEvent : BattleEvent
  {
    for (int i = 0; i < _events.Count; i++)
    {
      if (_events[i] is TEvent)
        return i;
    }

    return -1;
  }
}
