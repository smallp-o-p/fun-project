using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Owns the turn/round scheduling state: the live round queue, the sides that already acted
// this round, the active faction's available units, and the current turn (active faction +
// round number together). Constructed once from the complete prepared state against the
// authoritative ordered faction list and live consciousness sources, and valid on return:
// round 1, every supplied faction queued in order, first faction active, its conscious-unit
// availability populated. It deliberately knows nothing about events, outcomes, or death
// side-effects — the running receiver keeps that orchestration. Advancement owns the queue,
// round, current turn, and availability together; absence (None) retains the outgoing
// current turn so the receiver can settle Draw against it.
internal sealed class TurnScheduler
{
  private readonly IList<Faction> _orderedFactions;
  private readonly Func<Faction, bool> _hasConsciousUnits;
  private readonly Func<Faction, IEnumerable<BattleUnitState>> _consciousUnitsOf;

  private Queue<Faction> _turnQueue;
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];

  internal TurnScheduler(
    IList<Faction> orderedFactions,
    Func<Faction, bool> hasConsciousUnits,
    Func<Faction, IEnumerable<BattleUnitState>> consciousUnitsOf)
  {
    _orderedFactions = orderedFactions ?? throw new ArgumentNullException(nameof(orderedFactions));
    _hasConsciousUnits = hasConsciousUnits ?? throw new ArgumentNullException(nameof(hasConsciousUnits));
    _consciousUnitsOf = consciousUnitsOf ?? throw new ArgumentNullException(nameof(consciousUnitsOf));
    if (orderedFactions.Count == 0)
      throw new ArgumentException("The scheduler requires at least one faction.", nameof(orderedFactions));

    _turnQueue = new Queue<Faction>(orderedFactions);
    ActiveSide = _turnQueue.Peek();
    RoundNumber = 1;
    RefreshActiveFactionAvailability();
  }

  internal Faction ActiveSide { get; private set; }
  internal int RoundNumber { get; private set; }
  internal BattleTurn CurrentTurn => new(ActiveSide, RoundNumber);

  // One cohesive transition: refuse the whole transition when no conscious forces remain
  // anywhere (None) so the installed queue/acted/clock keep the outgoing turn coherent for
  // the Draw settlement; otherwise mark the outgoing side acted, drop its queue head, prune
  // eliminated sides, roll the round (rebuilding from conscious sides) when drained, select
  // the next side, and establish its availability.
  internal Option<BattleTurn> AdvanceTurn()
  {
    if (!_orderedFactions.AsValueEnumerable().Any(_hasConsciousUnits))
      return None;

    _sidesActedThisRound.Add(ActiveSide);
    _turnQueue.Dequeue();
    _turnQueue = new Queue<Faction>(_turnQueue.AsValueEnumerable().Where(_hasConsciousUnits).ToArray());

    if (_turnQueue.Count == 0)
    {
      if (!_orderedFactions.AsValueEnumerable().Any(_hasConsciousUnits))
        return None;

      RoundNumber++;
      _sidesActedThisRound.Clear();
      _turnQueue = new Queue<Faction>(_orderedFactions.AsValueEnumerable().Where(_hasConsciousUnits).ToArray());
    }

    ActiveSide = _turnQueue.Peek();
    RefreshActiveFactionAvailability();
    return Some(CurrentTurn);
  }

  // In-progress reinforcement: the scheduler owns the authoritative ordered faction list
  // (the shared state list — candidates register even when dead/unconscious), then folds an
  // unacted conscious side into the round and makes active-side joiners available
  // immediately. Already-acted sides wait for the next round — acted history clears only
  // at a real round rollover, so elimination cannot rejoin a side into the same round.
  internal void RegisterReinforcement(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Faction side = unit.Side;
    if (!_orderedFactions.Contains(side))
      _orderedFactions.Add(side);

    if (!unit.IsAlive || unit.IsUnconscious)
      return;

    if (side == ActiveSide)
    {
      _activeFactionUnitsAvailable.Add(unit);
      return;
    }

    if (_sidesActedThisRound.Contains(side) || _turnQueue.Contains(side))
      return;

    _turnQueue.Enqueue(side);
  }

  internal bool ConsumeActivation(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _activeFactionUnitsAvailable.Remove(unit);
  }

  // Scheduler-side reaction to a faction losing consciousness: the acted set is untouched
  // (it clears only at a real round rollover), and the faction is either pruned from the
  // queue or — when it is the active side mid-turn — its active availability clears so its
  // remaining activations end. Eliminating the active faction leaves its current turn
  // installed until it ends.
  internal void ReconcileConsciousness(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (_hasConsciousUnits(unit.Side))
      return;

    if (unit.Side == ActiveSide)
      _activeFactionUnitsAvailable.Clear();
    else
      _turnQueue = new Queue<Faction>(_turnQueue.AsValueEnumerable().Where(faction => faction != unit.Side).ToArray());
  }

  internal bool IsUnitAvailable(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _activeFactionUnitsAvailable.Contains(unit);
  }

  private void RefreshActiveFactionAvailability()
  {
    _activeFactionUnitsAvailable.Clear();
    foreach (BattleUnitState unit in _consciousUnitsOf(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
  }
}
