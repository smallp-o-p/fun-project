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
  private readonly Func<Faction, bool> _hasLivingUnits;
  private readonly Func<Faction, IEnumerable<BattleUnitState>> _aliveUnitsOf;

  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];

  public TurnScheduler(
    Func<Faction, bool> hasLivingUnits,
    Func<Faction, IEnumerable<BattleUnitState>> aliveUnitsOf)
  {
    _hasLivingUnits = hasLivingUnits ?? throw new ArgumentNullException(nameof(hasLivingUnits));
    _aliveUnitsOf = aliveUnitsOf ?? throw new ArgumentNullException(nameof(aliveUnitsOf));
  }

  public Faction ActiveSide { get; private set; } = null!;
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

  // Setup-time round queue seed (constructor): build the queue from the full global order
  // and point ActiveSide at its head, before any living-unit filtering applies.
  public void InitializeQueueFromGlobalOrder()
  {
    _turnQueue = new Queue<Faction>(_globalFactionOrder);
    ActiveSide = _turnQueue.Peek();
  }

  public void RebuildRoundQueueFromLivingSides()
  {
    _turnQueue.Clear();

    foreach (var side in _globalFactionOrder)
    {
      if (_hasLivingUnits(side))
        _turnQueue.Enqueue(side);
    }
  }

  public void SetActiveSideToQueueHead()
  {
    ActiveSide = _turnQueue.Peek();
  }

  public void ClearSidesActedThisRound()
  {
    _sidesActedThisRound.Clear();
  }

  public void RefreshActiveFactionAvailability()
  {
    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in _aliveUnitsOf(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
  }

  // Setup-time spawn: ensure the spawned unit's side is queued for the (not yet started) round.
  public void EnqueueSideIfAbsent(Faction side)
  {
    if (!_turnQueue.Contains(side))
      _turnQueue.Enqueue(side);
  }

  // In-progress spawn: fold the unit into the current round (active-faction availability if
  // it joins the active side, otherwise queue its side unless it already acted or is queued).
  public void AddSpawnedUnit(BattleUnitState unit)
  {
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
  // clear the active availability set so its remaining activations end. The session owns the
  // phase/active decision (isActiveInProgress) plus all death/outcome side-effects.
  public void OnFactionEliminated(Faction side, bool isActiveInProgress)
  {
    _sidesActedThisRound.Remove(side);
    if (!isActiveInProgress)
    {
      RemoveSideFromQueue(side);
      return;
    }

    _activeFactionUnitsAvailable.Clear();
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
    _turnQueue = new Queue<Faction>(_turnQueue.AsValueEnumerable().Where(_hasLivingUnits).ToArray());
  }

  private void RemoveSideFromQueue(Faction side)
  {
    _turnQueue = new Queue<Faction>(_turnQueue.AsValueEnumerable().Where(faction => faction != side).ToArray());
  }
}
