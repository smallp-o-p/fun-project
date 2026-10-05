using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Owns the turn/round scheduling state extracted from BattleSession: the global faction
// order, the live round queue, the set of sides that have already acted this round, the
// availability set for the active faction, and the ActiveSide pointer. It deliberately
// knows nothing about events, outcome, the battle phase, or death/loss side-effects — the
// session keeps that orchestration and calls into these pure queue/availability operations.
// Operations that depend on the live unit pool (session state) are injected as delegates.
internal sealed class TurnScheduler
{
  private readonly Func<Faction, bool> _hasConsciousUnits;
  private readonly Func<Faction, IEnumerable<BattleUnitState>> _eligibleUnitsOf;

  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];

  // A constructed scheduler is immediately valid: the global faction order keeps the first
  // occurrence of each identity, the first-round queue holds the conscious factions in that
  // order, the active side is the queue head, and its availability is established. A roster
  // that cannot start is a construction failure.
  public TurnScheduler(
    IEnumerable<Faction> factionOrder,
    Func<Faction, bool> hasConsciousUnits,
    Func<Faction, IEnumerable<BattleUnitState>> eligibleUnitsOf)
  {
    ArgumentNullException.ThrowIfNull(factionOrder);
    _hasConsciousUnits = hasConsciousUnits ?? throw new ArgumentNullException(nameof(hasConsciousUnits));
    _eligibleUnitsOf = eligibleUnitsOf ?? throw new ArgumentNullException(nameof(eligibleUnitsOf));

    foreach (var faction in factionOrder)
      RegisterFaction(faction);

    RebuildRoundQueueFromConsciousSides();
    if (_turnQueue.Count == 0)
      throw new InvalidOperationException(
        "Cannot start a battle without at least one conscious faction in the session.");
    ActiveSide = _turnQueue.Peek();
    RefreshActiveFactionAvailability();
  }

  public Faction ActiveSide { get; private set; }
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _globalFactionOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _turnQueue;
  public int RoundQueueCount => _turnQueue.Count;

  // Registers a faction in the global turn order. Returns true iff it was newly added.
  public bool RegisterFaction(Faction side)
  {
    if (_globalFactionOrder.Contains(side))
      return false;

    _globalFactionOrder.Enqueue(side);
    return true;
  }

  public void RebuildRoundQueueFromConsciousSides()
  {
    _turnQueue.Clear();

    foreach (var side in _globalFactionOrder)
    {
      if (_hasConsciousUnits(side))
        _turnQueue.Enqueue(side);
    }
  }

  public void ClearSidesActedThisRound()
  {
    _sidesActedThisRound.Clear();
  }

  public void RefreshActiveFactionAvailability()
  {
    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in _eligibleUnitsOf(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
  }

  // In-progress spawn: fold the unit into the current round (active-faction availability if
  // it joins the active side, otherwise queue its side unless it already acted or is queued).
  public void AddSpawnedUnit(BattleUnitState unit)
  {
    if (!unit.IsAlive || unit.IsUnconscious)
      return;

    if (unit.Side == ActiveSide)
    {
      _activeFactionUnitsAvailable.Add(unit);
      return;
    }

    if (_sidesActedThisRound.Contains(unit.Side))
      return;
    if (_turnQueue.Contains(unit.Side))
      return;

    _turnQueue.Enqueue(unit.Side);
  }

  public void MarkActiveSideActed()
  {
    _sidesActedThisRound.Add(ActiveSide);
  }

  // Dequeue the (just-finished) head, drop any sides eliminated mid-round, and report
  // whether the round is now over (queue drained) so the session can pick round-vs-next-side.
  public bool AdvanceToNextSide()
  {
    _turnQueue.Dequeue();
    RemoveEliminatedSidesFromQueue();
    return _turnQueue.Count == 0;
  }

  // Point ActiveSide at the new queue head and return it (the session refreshes that side's
  // units and raises the turn-start events around this call).
  public Faction AdvanceActiveSideToQueueHead()
  {
    ActiveSide = _turnQueue.Peek();
    return ActiveSide;
  }

  // Scheduler-side reaction to a faction being wiped out: it has no more turns. Drop it from
  // the acted set, then either prune it from the queue or — if it is the active side mid-turn —
  // clear the active availability set so its remaining activations end. Active-side identity
  // is a scheduler-owned fact.
  public void OnFactionEliminated(Faction side)
  {
    _sidesActedThisRound.Remove(side);
    if (side == ActiveSide)
    {
      _activeFactionUnitsAvailable.Clear();
      return;
    }

    RemoveSideFromQueue(side);
  }

  public bool TryConsumeAvailableUnit(BattleUnitState unit)
  {
    return _activeFactionUnitsAvailable.Remove(unit);
  }

  public bool IsUnitAvailable(BattleUnitState unit)
  {
    return _activeFactionUnitsAvailable.Contains(unit);
  }

  public void ClearActiveFactionAvailability()
  {
    _activeFactionUnitsAvailable.Clear();
  }

  public void ClearTurnQueue()
  {
    _turnQueue.Clear();
  }

  private void RemoveEliminatedSidesFromQueue()
  {
    _turnQueue = new Queue<Faction>(_turnQueue.AsValueEnumerable().Where(_hasConsciousUnits).ToArray());
  }

  private void RemoveSideFromQueue(Faction side)
  {
    _turnQueue = new Queue<Faction>(_turnQueue.AsValueEnumerable().Where(faction => faction != side).ToArray());
  }
}
