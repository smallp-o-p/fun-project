using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Lifecycle-independent tactical storage shared by preparation, running combat, completion
// capture, and cross-lifecycle reads. It deliberately holds no scheduler, turn number, or
// outcome — preparation and the running receiver layer those on around it.
internal sealed class BattleState
{
  private readonly List<Faction> _factions = [];
  private readonly List<BattleUnitState> _units = [];
  private readonly SysColGeneric.List<BattleObjectState> _objects = [];
  private readonly Dictionary<Faction, List<Objective>> _objectives = [];
  private readonly Dictionary<BattleUnitState, List<BattleUnitState>> _killsByUnit = [];
  private readonly VisibilityService _visibility = new();
  private readonly SysColGeneric.HashSet<BattleUnitState> _visibilityAffectedUnits = [];
  private readonly IHitChanceCalculator _hitChanceCalculator;
  private readonly Random _random;

  // Visible sets depend on board occupancy and consciousness: a unit's vision range resolves from
  // stat contributions (combatant + equipped weapon + active buffs; no action swaps weapons
  // or equips mods mid-battle, and buff evaluation marks each flipped unit affected), and
  // tile BlocksLineOfSight is set during setup. So visibility only needs recomputing after
  // an occupancy, consciousness, or buff-flip mutation, at two granularities:
  //   - _visibilityFullRefreshPending forces a clear-and-recompute-everyone pass. Used on
  //     battle start, where tile BlocksLineOfSight authoring may have changed without any
  //     occupancy event (so no affected-unit set could capture it). Starts true so the first
  //     dispatch performs the initial compute.
  //   - _visibilityAffectedUnits accumulates the units whose own cell changed (move/spawn/
  //     death), who became unconscious, or whose buff evaluation flipped since the last
  //     refresh. The incremental pass recomputes those units and their visibility to others,
  //     matching a full recompute: an unaffected observer's visible tiles cannot change when
  //     another unit changes.
  // Mark affected units at Board.Try* occupancy chokepoints and unconscious transitions;
  // battle start requests a full refresh. Likewise, any runtime mutation to a tile's
  // BlocksLineOfSight or BlocksVerticalLineOfSight (e.g. destructible terrain) must call
  // InvalidateVisibility() — tile flag changes are NOT occupancy events and are not
  // otherwise caught by the refresh machinery.
  private bool _visibilityFullRefreshPending = true;

  // The one shared event dispatcher: every preparation placement, gameplay event, and
  // completion event queues through here, and each committed event is broadcast once to
  // Committed subscribers after pending visibility work resolves.
  private readonly Queue<BattleEvent> _eventDispatchQueue = [];
  private bool _isDispatchingEvents;

  internal BattleState(
    BattleBoardState board,
    IEnumerable<Faction> factions,
    IHitChanceCalculator? hitChanceCalculator = null,
    int? randomSeed = null,
    Option<Faction> playerFaction = default)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(factions);

    Board = board;
    PlayerFaction = playerFaction;
    _hitChanceCalculator = hitChanceCalculator ?? new StandardHitChanceCalculator();
    _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);

    foreach (var faction in factions)
    {
      RegisterFaction(faction);
    }
  }

  internal BattleBoardState Board { get; }
  internal Option<Faction> PlayerFaction { get; }
  internal IList<Faction> Factions => _factions;
  internal IHitChanceCalculator HitChanceCalculator => _hitChanceCalculator;
  internal UnitActionCache ActionOptions { get; } = new();

  // Raw views: snapshot enumerables of plain unit/object state; proofs exist only as return
  // values (TryGetAlive, read-query results), never as stored collections.
  internal IEnumerable<BattleUnitState> AliveUnits => _units.AsValueEnumerable().Where(unit => unit.IsAlive).ToArray();
  internal IEnumerable<BattleUnitState> DeadUnits => _units.AsValueEnumerable().Where(unit => unit.IsDead).ToArray();
  internal IEnumerable<BattleObjectState> Objects => _objects.AsValueEnumerable().ToArray();

  // Read-only view over the same pool: completion capture needs the whole participant set in
  // one grouped pass, which the separate Alive/Dead snapshots cannot give. No second storage.
  internal SysColGeneric.IReadOnlyList<BattleUnitState> Units => _units;

  /// <summary>One committed battle event: pending visibility work resolves first (queueing
  /// first-time spottings), then the event broadcasts to Committed subscribers. The stream
  /// stays linear: events raised while dispatching run after the current one.</summary>
  internal void RaiseEvents(params BattleEvent[] events)
  {
    foreach (var battleEvent in events)
    {
      ArgumentNullException.ThrowIfNull(battleEvent);
      _eventDispatchQueue.Enqueue(battleEvent);
    }

    DispatchQueuedEvents();
  }

  internal event Action<BattleEvent> Committed = delegate { };

  // Buff flips evaluated by running hooks (turn start/spawn) raise through here: while an
  // outer dispatch is already running, RaiseEvents would only enqueue, leaving the flip's
  // pending visibility unresolved until the queue drains — the next grant's condition read
  // in the same pass would see stale visible/explored sets. Enqueue first (the grant's event
  // precedes any first spots it discovers), resolve pending visibility through the ordinary
  // refresh-and-queue pass, then dispatch when allowed; while already dispatching this
  // returns and the outer loop drains everything in FIFO order.
  internal void RaiseBuffEvent(BattleEvent battleEvent)
  {
    ArgumentNullException.ThrowIfNull(battleEvent);
    _eventDispatchQueue.Enqueue(battleEvent);
    RefreshVisibilityAndQueueSpottings();
    DispatchQueuedEvents();
  }

  private void DispatchQueuedEvents()
  {
    if (_isDispatchingEvents)
      return;

    _isDispatchingEvents = true;
    try
    {
      while (_eventDispatchQueue.Count > 0)
      {
        BattleEvent battleEvent = _eventDispatchQueue.Dequeue();
        // Recompute visibility from any occupancy/tile change since the last dispatch and queue
        // first-time spottings, before this event is broadcast (preserves the mid-move guarantee).
        RefreshVisibilityAndQueueSpottings();

        Committed.Invoke(battleEvent);
      }
    }
    catch
    {
      _eventDispatchQueue.Clear();
      throw;
    }
    finally
    {
      _isDispatchingEvents = false;
    }
  }

  private void RefreshVisibilityAndQueueSpottings()
  {
    foreach (var (observer, target) in RefreshVisibility())
      _eventDispatchQueue.Enqueue(new UnitSpottedBattleEvent(observer, target));
  }

  // Adds the faction to the ordered list unless already present; returns true iff newly added.
  internal bool RegisterFaction(Faction side)
  {
    if (_factions.Contains(side))
      return false;

    _factions.Add(side);
    return true;
  }

  internal BattleUnitState AddUnit(
    Combatant combatant,
    BattleBoardState.ValidatedPoint position,
    Option<Weapon> weapon,
    Option<ItemWith<ArmorCapability>> armor,
    IReadOnlyList<StatMod>? statMods = null)
  {
    ArgumentNullException.ThrowIfNull(combatant);

    var unit = new BattleUnitState(_units.Count, combatant, weapon, armor, statMods);
    if (!Board.TryPlaceOccupant(position, unit.Id))
      throw new InvalidOperationException($"Could not place unit {unit.Id} at {position.Raw}.");
    MarkVisibilityAffected(unit);

    _units.Add(unit);
    return unit;
  }

  // Trusted core: the caller pre-validates occupancy; a miss here is a caller bug.
  internal BattleObjectState AddObject(BattleSpecialObjectData data, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(data);
    var state = new BattleObjectState(-_objects.Count - 1, data, position.Raw);
    if (!Board.TryPlaceObjectOccupant(position, state.Id))
      throw new InvalidOperationException($"Object cell {position.Raw} is not occupiable.");
    _objects.Add(state);
    return state;
  }

  internal IEnumerable<BattleUnitState> GetFactionAliveUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _units.AsValueEnumerable().Where(unit => unit.IsAlive && unit.Side == side).ToArray();
  }

  internal IEnumerable<BattleUnitState> GetFactionConsciousUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _units.AsValueEnumerable()
      .Where(unit => unit.Side == side && unit.IsAlive && !unit.IsUnconscious).ToArray();
  }

  // A boolean, not a snapshot: no effect runs inside this predicate, so the faction's
  // living conscious members are answered directly from the pool without materializing
  // intermediate faction snapshots.
  internal bool HasConsciousUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _units.AsValueEnumerable().Any(unit =>
      unit.Side == side && unit.IsAlive && !unit.IsUnconscious);
  }

  // Mints a proof iff the unit instance belongs to this storage's alive pool (provenance +
  // aliveness in one check). The single door for callers holding a raw BattleUnitState.
  internal Option<AliveUnit> TryGetAlive(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _units.Contains(unit) && unit.IsAlive ? Some(MintAlive(unit)) : None;
  }

  internal Option<LiveObject> TryGetAliveObject(BattleObjectState obj)
  {
    ArgumentNullException.ThrowIfNull(obj);
    return _objects.Contains(obj) && obj.Status.IsNone
      ? Some(MintAliveObject(obj))
      : None;
  }

  // Single mint point: snapshots the unit's board position into the one-shot proof. An alive
  // pooled unit is always board-indexed (visibility Refresh invariant), so a miss here is a
  // state bug, not a caller error. Callers must pass a unit already known alive-in-battle.
  internal AliveUnit MintAlive(BattleUnitState unit)
  {
    return GetUnitPosition(unit).Match(
      Some: position => new AliveUnit(unit, position),
      None: () => throw new InvalidOperationException($"Unit {unit.Id} is alive but not board-indexed."));
  }

  internal LiveObject MintAliveObject(BattleObjectState obj)
    => Board.FindObjectPosition(obj.Id).Match(
      Some: position => new LiveObject(obj, position),
      None: () => throw new InvalidOperationException($"Object {obj.Id} is placed but not board-indexed."));

  internal DeadUnit MintDead(BattleUnitState unit)
  {
    return new DeadUnit(unit);
  }

  // Attack-target mint door: Some iff the entity is currently a targetable member of this
  // battle — a live unit, or a live object carrying health. The proof is a snapshot
  // (membership + position at mint) that may go stale across an executor commit.
  internal Option<AttackTarget> TryGetAttackTarget(BattleEntity entity)
  {
    ArgumentNullException.ThrowIfNull(entity);
    return entity switch
    {
      BattleEntity.Unit unit => TryGetAlive(unit.State)
        .Map(alive => new AttackTarget(entity, alive.Position)),
      BattleEntity.Object obj => TryGetAliveObject(obj.State)
        .Bind(live => obj.State.FindCapability<ObjectHealthCapability>()
          .Map(_ => new AttackTarget(entity, live.Position))),
      _ => throw new InvalidOperationException("Unknown battle entity."),
    };
  }

  internal Option<BattleBoardState.ValidatedPoint> GetUnitPosition(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return Board.FindOccupantPosition(unit.Id);
  }

  internal Option<BattleUnitState> GetUnitAt(BattleBoardState.ValidatedPoint point)
  {
    return Board.GetOccupant(point).Bind(id =>
      id < _units.Count && _units[id].IsAlive ? Some(_units[id]) : None);
  }

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetFactionExploredTiles(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _visibility.GetExploredTiles(side);
  }

  internal bool IsUnitVisibleToFaction(Faction side, BattleUnitState target)
  {
    ArgumentNullException.ThrowIfNull(side);
    ArgumentNullException.ThrowIfNull(target);

    if (target.Side == side)
      return true;

    return GetFactionAliveUnits(side)
      .AsValueEnumerable().Any(unit => unit.VisibleUnits.Contains(target));
  }

  internal bool IsTileVisibleToFaction(Faction side, BattleBoardState.ValidatedPoint tile)
  {
    ArgumentNullException.ThrowIfNull(side);
    return GetFactionAliveUnits(side).AsValueEnumerable().Any(unit => unit.VisibleTiles.Contains(tile));
  }

  // Records a unit whose board cell or consciousness changed so the next dispatch
  // can scope its visibility recompute to the affected units instead of the whole pool. A
  // pending full refresh (battle start) still takes priority and clears this set.
  internal void MarkVisibilityAffected(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    _visibilityAffectedUnits.Add(unit);
  }

  // Forces a full clear-and-recompute of all faction visibility on the next event dispatch.
  // Call this after any runtime mutation to a tile's BlocksLineOfSight or
  // BlocksVerticalLineOfSight (e.g. destructible terrain) — those flag changes are NOT
  // occupancy events and are not otherwise caught by the refresh machinery.
  internal void InvalidateVisibility()
  {
    _visibilityFullRefreshPending = true;
  }

  // Resolves pending visibility work for one event dispatch: a pending full recompute (e.g.
  // battle start) wins over the affected-unit set. Returns first-time spot pairs in
  // deterministic order; the caller queues their events before the next broadcast.
  internal IReadOnlyList<(BattleUnitState Observer, BattleUnitState Target)> RefreshVisibility()
  {
    IReadOnlyList<(BattleUnitState Observer, BattleUnitState Target)> firstSpottings;
    if (_visibilityFullRefreshPending)
    {
      firstSpottings = _visibility.RefreshAllUnits(Board, _units, AliveUnits);
      _visibilityFullRefreshPending = false;
      _visibilityAffectedUnits.Clear();
    }
    else if (_visibilityAffectedUnits.Count > 0)
    {
      firstSpottings = _visibility.RefreshAffected(Board, _units, _visibilityAffectedUnits);
      _visibilityAffectedUnits.Clear();
    }
    else
    {
      firstSpottings = [];
    }

    return firstSpottings;
  }

  // Adds the objective to the faction's list, creating the list on first use, and registers
  // the faction for turns (idempotent).
  internal void AddObjective(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);

    if (!_objectives.TryGetValue(faction, out var list))
      _objectives[faction] = list = [];

    list.Add(objective);
    RegisterFaction(faction);
  }

  internal IReadOnlyList<Objective> GetObjectives(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    return _objectives.TryGetValue(faction, out var list) ? list : [];
  }

  internal void RecordKill(BattleUnitState killer, BattleUnitState killed)
  {
    _killsByUnit.TryAdd(killer, []);
    _killsByUnit[killer].Add(killed);
  }

  internal IReadOnlyDictionary<BattleUnitState, List<BattleUnitState>> KillsByUnit => _killsByUnit;

  internal int RollPercent()
  {
    return _random.Next(100);
  }
}
