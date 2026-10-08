using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Owns the turn/round scheduling state extracted from BattleSession: the global faction
// order, the live round queue, the set of sides that have already acted this round, the
// availability set for the active faction, and the ActiveSide pointer. It deliberately
// knows nothing about events, outcome, or death/loss side-effects — the session wraps
// Advance() with the turn events, the turn number, and outcome policy. Operations that
// depend on the live unit pool (session state) are injected as delegates.
internal sealed class TurnScheduler
{
  private readonly BattleAccessBinding _battleAccess = new();
  internal void BindBattleAccess(BattleAccessGuard access) { _battleAccess.Bind(access); }
  internal void UnbindBattleAccess(BattleAccessGuard access) => _battleAccess.Unbind(access);

  private readonly Func<Faction, bool> _hasConsciousUnits;
  private readonly Func<Faction, IEnumerable<BattleUnitState>> _eligibleUnitsOf;

  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];

  // One completed scheduling transition. Exhausted means no eligible faction remains; the
  // retained last active side is still reported so historical reads stay total.
  internal readonly record struct AdvanceResult(Faction NextSide, bool NextRound, bool Exhausted);

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

  private Faction _activeSide = null!;
  public Faction ActiveSide
  {
    get { _battleAccess.RequireRead(); return _activeSide; }
    private set { _battleAccess.RequireMutation(); _activeSide = value; }
  }
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder { get { _battleAccess.RequireRead(); return _globalFactionOrder.AsValueEnumerable().ToArray(); } }
  public IReadOnlyCollection<Faction> TurnQueue { get { _battleAccess.RequireRead(); return _turnQueue.AsValueEnumerable().ToArray(); } }

  // Registers a faction in the global turn order. Returns true iff it was newly added.
  public bool RegisterFaction(Faction side)
  {
    _battleAccess.RequireMutation();
    if (_globalFactionOrder.Contains(side))
      return false;

    _globalFactionOrder.Enqueue(side);
    return true;
  }

  // Complete subsequent transition: the outgoing side acted and leaves the queue, ineligible
  // sides are pruned, a drained round is rebuilt in global faction order with fresh acted
  // history, and the next head's availability is established before returning. With no
  // eligible faction left, availability is cleared and the result is Exhausted — the last
  // active identity is retained and no active faction is invented.
  public AdvanceResult Advance()
  {
    _battleAccess.RequireMutation();
    _sidesActedThisRound.Add(ActiveSide);
    _turnQueue.Dequeue();
    RemoveEliminatedSidesFromQueue();

    bool nextRound = _turnQueue.Count == 0;
    if (nextRound)
    {
      _sidesActedThisRound.Clear();
      RebuildRoundQueueFromConsciousSides();
    }

    if (_turnQueue.Count == 0)
    {
      _activeFactionUnitsAvailable.Clear();
      return new AdvanceResult(ActiveSide, NextRound: false, Exhausted: true);
    }

    ActiveSide = _turnQueue.Peek();
    RefreshActiveFactionAvailability();
    return new AdvanceResult(ActiveSide, nextRound, Exhausted: false);
  }

  // In-progress spawn: fold the unit into the current round (active-faction availability if
  // it joins the active side, otherwise queue its side unless it already acted or is queued).
  public void AddSpawnedUnit(BattleUnitState unit)
  {
    _battleAccess.RequireMutation();
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

  // Scheduler-side reaction to a faction being wiped out: it has no more turns. Drop it from
  // the acted set, then either prune it from the queue or — if it is the active side mid-turn —
  // clear the active availability set so its remaining activations end. The active head is
  // kept until explicit end-turn; active-side identity is a scheduler-owned fact.
  public void OnFactionEliminated(Faction side)
  {
    _battleAccess.RequireMutation();
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
    _battleAccess.RequireMutation();
    return _activeFactionUnitsAvailable.Remove(unit);
  }

  public bool IsUnitAvailable(BattleUnitState unit)
  {
    _battleAccess.RequireRead();
    return _activeFactionUnitsAvailable.Contains(unit);
  }

  // Clears pending turns and availability without erasing the last active-side identity, so
  // completed battles keep historical reads total.
  public void Stop()
  {
    _battleAccess.RequireMutation();
    _turnQueue.Clear();
    _activeFactionUnitsAvailable.Clear();
  }

  private void RebuildRoundQueueFromConsciousSides()
  {
    _turnQueue.Clear();

    foreach (var side in _globalFactionOrder)
    {
      if (_hasConsciousUnits(side))
        _turnQueue.Enqueue(side);
    }
  }

  private void RefreshActiveFactionAvailability()
  {
    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in _eligibleUnitsOf(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
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
