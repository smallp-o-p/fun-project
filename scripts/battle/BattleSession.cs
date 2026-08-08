using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Weapons;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public enum BattlePhase
{
  Setup,
  InProgress,
  Ended,
}

public sealed class BattleSession
{
  internal readonly record struct SpawnedBattleUnit(BattleUnitState Unit);

  public const int DefaultMovementStepActionPointCost = 1;
  public const int DefaultAttackActionPointCost = 1;
  public const int DefaultReloadActionPointCost = 1;
  public const int DefaultUseItemActionPointCost = 1;

  private readonly IHitChanceCalculator _hitChanceCalculator;
  private readonly Random _random;
  private readonly List<BattleUnitState> _units = [];
  private readonly VisibilityService _visibility = new();
  private readonly Dictionary<Faction, Operation> _operations = [];
  private readonly TurnScheduler _scheduler;
  private readonly Queue<BattleEvent> _eventDispatchQueue = [];


  private bool _isDispatchingEvents;

  // Visible sets depend only on board occupancy: a unit's vision range resolves from
  // stat contributions that are fixed for the battle (combatant + equipped weapon; no
  // action swaps weapons, equips mods, or applies a vision-affecting effect mid-battle),
  // and tile BlocksLineOfSight is set during setup. So visibility only needs recomputing
  // after an occupancy mutation, at two granularities:
  //   - _visibilityFullRefreshPending forces a clear-and-recompute-everyone pass. Used on
  //     battle start, where tile BlocksLineOfSight authoring may have changed without any
  //     occupancy event (so no affected-unit set could capture it). Starts true so the first
  //     dispatch performs the initial compute.
  //   - _visibilityAffectedUnits accumulates the units whose own cell changed (move/spawn/
  //     death) since the last refresh. An incremental pass then recomputes only those units
  //     and their visibility to others — byte-identical to a full recompute, because an
  //     unaffected observer's visible-tile set cannot change when some other unit moves.
  // Both are written at the three Board.Try* occupancy chokepoints (affected) and on battle
  // start (full). NOTE: if a runtime effect that changes a unit's vision is ever added, force
  // a full refresh (or mark that unit affected) there too. Likewise, any runtime mutation to
  // a tile's BlocksLineOfSight or BlocksVerticalLineOfSight (e.g. destructible terrain) must
  // call InvalidateVisibility() — tile flag changes are NOT occupancy events and are not
  // otherwise caught by the refresh machinery.
  private bool _visibilityFullRefreshPending = true;
  private readonly SysColGeneric.HashSet<BattleUnitState> _visibilityAffectedUnits = [];

  public BattleBoardState Board { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction ActiveSide => _scheduler.ActiveSide;
  public Option<Faction> PlayerFaction { get; }
  public Option<BattleOutcome> Outcome { get; private set; }
  // Raw views: the session holds and exposes plain unit state; proofs exist only as return
  // values (TryGetAlive, read-query results), never as session-held collections.
  public IEnumerable<BattleUnitState> AliveUnits => _units.Where(unit => unit.IsAlive);
  public IEnumerable<BattleUnitState> DeadUnits => _units.Where(unit => unit.IsDead);

  // Mints a proof iff the unit instance belongs to THIS session's alive storage (provenance +
  // aliveness in one check). The single door for callers holding a raw BattleUnitState.
  public Option<AliveUnit> TryGetAlive(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _units.Contains(unit) && unit.IsAlive ? Some(MintAlive(unit)) : None;
  }

  // Single mint point: snapshots the unit's board position into the one-shot proof. An alive
  // session unit is always board-indexed (visibility Refresh invariant), so a miss here is a
  // session bug, not a caller error. Callers must pass a unit already known alive-in-session.
  internal AliveUnit MintAlive(BattleUnitState unit)
  {
    return GetUnitPosition(unit).Match(
      Some: position => new AliveUnit(unit, position),
      None: () => throw new InvalidOperationException($"Unit {unit.Id} is alive but not board-indexed."));
  }

  internal DeadUnit MintDead(BattleUnitState unit)
  {
    return new DeadUnit(unit);
  }
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _scheduler.GlobalFactionTurnOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _scheduler.TurnQueue;
  private readonly Dictionary<BattleUnitState, List<BattleUnitState>> _killsByUnit = [];

  public event Action<BattleEvent> BattleEventCommitted = delegate { };

  public BattleSession(
    BattleBoardState board,
    IEnumerable<Faction> globalFactionOrder,
    IHitChanceCalculator? hitChanceCalculator = null,
    int? randomSeed = null,
    Option<Faction> playerFaction = default)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(globalFactionOrder);

    _hitChanceCalculator = hitChanceCalculator ?? new StandardHitChanceCalculator();
    _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);

    Board = board;
    PlayerFaction = playerFaction;
    _scheduler = new TurnScheduler(HasLivingUnits, GetFactionAliveUnits);

    foreach (var faction in globalFactionOrder)
    {
      EnqueueFactionInGlobalOrder(faction);
    }

    if (_scheduler.GlobalFactionTurnOrder.Count == 0)
      throw new ArgumentException("Battle session requires at least one faction in the global order.");

    PlayerFaction.IfSome(EnqueueFactionInGlobalOrder);

    _scheduler.InitializeQueueFromGlobalOrder();
  }

  internal IEnumerable<BattleUnitState> GetFactionAliveUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return AliveUnits.Where(unit => unit.Side == side);
  }

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetFactionVisibleTiles(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return GetFactionAliveUnits(side)
      .SelectMany(unit => unit.VisibleTiles)
      .ToHashSet();
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
      .Any(unit => unit.VisibleUnits.Contains(target));
  }

  internal bool IsTileVisibleToFaction(Faction side, BattleBoardState.ValidatedPoint tile)
  {
    ArgumentNullException.ThrowIfNull(side);
    return GetFactionAliveUnits(side).Any(unit => unit.VisibleTiles.Contains(tile));
  }

  internal bool IsUnitStillAvailableThisTurn(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _scheduler.IsUnitAvailable(unit);
  }

  internal void StartBattle()
  {
    if (Phase != BattlePhase.Setup)
      throw new InvalidOperationException("Battle session can only be started from setup.");

    _scheduler.RebuildRoundQueueFromLivingSides();
    if (_scheduler.RoundQueueCount == 0)
      throw new InvalidOperationException("Cannot start a battle without at least one living faction in the session.");

    foreach (var side in _scheduler.GlobalFactionTurnOrder)
    {
      if (_operations[side].PendingObjectives.Count == 0)
        throw new InvalidOperationException(
          $"Faction {side.Name} has no objective; assign every faction at least one objective before starting the battle.");
    }

    Phase = BattlePhase.InProgress;
    TurnNumber = 1;
    _scheduler.SetActiveSideToQueueHead();
    _scheduler.ClearSidesActedThisRound();
    _scheduler.RefreshActiveFactionAvailability();

    // Force a FULL recompute so the start events perform an authoritative pass. Board
    // authoring that affects line of sight (tile BlocksLineOfSight) is finalized during
    // setup, possibly after the last spawn's refresh, and is not an occupancy change, so
    // only a full pass is guaranteed to pick it up.
    _visibilityFullRefreshPending = true;

    // Turn-start hooks (buffs) fire inside this dispatch, BEFORE the AP refresh below reads
    // buffed MaxActionPoints. Mid-dispatch observers see pre-refresh action points.
    RaiseEvents(
      new SessionStartedBattleEvent(),
      new TurnStartedBattleEvent(ActiveSide, TurnNumber));

    foreach (var unit in AliveUnits)
      unit.RefreshForNewTurn();
  }

  internal SpawnedBattleUnit AddUnit(
    Combatant combatant,
    BattleBoardState.ValidatedPoint position,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    if (Phase == BattlePhase.Ended)
      throw new InvalidOperationException("Cannot add units after the battle has ended.");

    var unit = new BattleUnitState(_units.Count, combatant, equippedWeapon, equippedArmor);
    bool occupantSet = Board.TryPlaceOccupant(position, unit.Id);
    if (!occupantSet)
      throw new InvalidOperationException($"Could not place unit {unit.Id} at {position.Raw}.");
    MarkVisibilityAffected(unit);

    _units.Add(unit);
    EnqueueFactionInGlobalOrder(unit.Side);

    if (Phase == BattlePhase.Setup)
      _scheduler.EnqueueSideIfAbsent(unit.Side);
    if (Phase == BattlePhase.InProgress)
      _scheduler.AddSpawnedUnit(unit);

    RaiseEvents(new UnitAddedBattleEvent(unit, position));

    return new SpawnedBattleUnit(unit);
  }

  internal void ApplyDamageTo(BattleUnitState unit, int amount)
    => ApplyDamageTo(unit, [new Damage(amount, Element.Kinetic)], None);

  internal void ApplyDamageTo(BattleUnitState unit, IReadOnlyList<Damage> bundle, Option<BattleUnitState> cause)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    if (unit.IsDead)
      throw new InvalidOperationException($"Cannot damage unit {unit.Id} because it is already dead.");
    if (Board.FindOccupantPosition(unit.Id).IsNone)
      throw new InvalidOperationException($"Cannot damage unit {unit.Id} because it is not on the board.");

    Option<ArmorState> armorState =
      unit.EquippedArmor.Map(armor => new ArmorState(armor.Capability.Current, armor.Capability.Element));
    IReadOnlyList<DamageResolution> packetResolutions = DamageResolver.ResolvePackets(bundle, armorState);
    DamageResolution resolution = DamageResolver.Resolve(packetResolutions);

    unit.EquippedArmor.IfSome(armor =>
    {
      armor.Capability.Reduce(resolution.ArmorDamage);
      if (resolution.ArmorDamage > 0 || resolution.HealthDamage > 0)
        armor.Capability.RearmRegenDelay();
    });
    unit.ReceiveDamage(resolution.HealthDamage);

    if (unit.IsDead)
    {
      HandleUnitDeath(unit, cause);
      return;
    }

    RaiseEvents(new UnitDamagedBattleEvent(unit, cause, bundle, resolution.ArmorDamage, resolution.HealthDamage));
    ApplyStatusEffectsFrom(unit, bundle, packetResolutions);
  }

  private void ApplyStatusEffectsFrom(
    BattleUnitState unit,
    IReadOnlyList<Damage> bundle,
    IReadOnlyList<DamageResolution> packetResolutions)
  {
    foreach ((Damage damage, DamageResolution resolution) in bundle.Zip(packetResolutions))
    {
      if (damage.Amount <= 0)
        continue;

      damage.Status.IfSome(spec =>
      {
        if (spec.RequiresHealthDamage && resolution.HealthDamage <= 0)
          return;

        TryApplyStatusEffect(unit, spec);
      });
    }
  }

  // Applies a status effect to a unit, gated by its ApplyChancePercent roll, and raises
  // the applied event. Shared by the damage pipeline (ApplyStatusEffectsFrom) and direct
  // application (ApplyStatusEffectTo). Callers are responsible for liveness and any
  // damage-pipeline gating (e.g. RequiresHealthDamage).
  private void TryApplyStatusEffect(BattleUnitState unit, StatusEffectSpecData spec)
  {
    if (spec.ApplyChancePercent < 100 && RollPercent() >= spec.ApplyChancePercent)
      return;

    ActiveStatusEffect applied = unit.ApplyStatusEffect(spec);
    RaiseEvents(new UnitStatusEffectAppliedBattleEvent(unit, spec, applied.RemainingTurns));
  }

  // Applies a pure status effect (no damage) directly to a unit: the entry point used by
  // capability/effect resolution (e.g. a thrown grenade's status payload). RequiresHealthDamage
  // is a damage-pipeline concern and does not apply here. Does nothing if the unit is dead.
  internal void ApplyStatusEffectTo(BattleUnitState unit, StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(spec);
    if (unit.IsDead)
      return;

    TryApplyStatusEffect(unit, spec);
  }

  private void HandleUnitDeath(BattleUnitState unit, Option<BattleUnitState> killedBy)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (unit.IsAlive)
      throw new InvalidOperationException($"Cannot remove unit {unit.Id} as dead because it is still alive.");

    var unitSide = unit.Side;
    Option<BattleBoardState.ValidatedPoint> unitPointOption = Board.FindOccupantPosition(unit.Id);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    bool occupantCleared = Board.TryClearOccupant(unitPoint, unit.Id);
    if (!occupantCleared)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} from {unitPoint.Raw}.");
    MarkVisibilityAffected(unit);

    _scheduler.TryConsumeAvailableUnit(unit);
    killedBy.IfSome(killer =>
    {
      _killsByUnit.TryAdd(killer, []);
      _killsByUnit[killer].Add(unit);
    });

    RaiseEvents(new UnitKilledBattleEvent(unit, unitPoint, killedBy));

    HandleFactionLoss(unitSide);

    if (Phase == BattlePhase.InProgress
        && PlayerFaction.Match(Some: player => player == unitSide, None: () => false)
        && !HasLivingUnits(unitSide))
      EndBattle(BattleOutcome.Defeat);
  }

  internal void EndUnitActivation(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot end a unit activation while the battle is not in progress.");

    var activeSide = ActiveSide;
    if (unit.Side != activeSide)
      throw new InvalidOperationException($"Unit {unit.Id} is not on the active side.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Cannot end activation for unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    if (!_scheduler.TryConsumeAvailableUnit(unit))
      throw new InvalidOperationException($"Unit {unit.Id} is not available this turn.");

    RaiseEvents(new UnitActivationEndedBattleEvent(unit, unitPoint));

    if (!GetFactionAliveUnits(activeSide).Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  private void AdvanceTurn()
  {
    RaiseEvents(new TurnEndedBattleEvent(
      ActiveSide,
      TurnNumber));

    // A turn-end DoT can wipe the player during the dispatch above, ending the
    // battle immediately; bail before mutating the turn queue.
    if (Phase == BattlePhase.Ended)
      return;

    // End-of-turn outcome. This runs after RaiseEvent above has fully drained the
    // dispatch queue, so the turn-end listeners (status -> armor -> ObjectiveSystem)
    // have updated operation statuses AND any reaction listeners (e.g. a fail ->
    // add-and-reactivate handler) have already run before we read the status.
    // Resolve before any turn-queue mutation.
    if (TryEndBattleIfDecided())
      return;

    _scheduler.MarkActiveSideActed();

    if (_scheduler.AdvanceToNextSide())
    {
      StartNextRound();
      return;
    }

    BeginNextQueuedSideTurn();
  }

  internal void EndFactionTurn(Faction expectedActiveSide)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot end a faction turn while the battle is not in progress.");

    var activeSide = ActiveSide;
    if (activeSide != expectedActiveSide)
      throw new InvalidOperationException(
        $"{expectedActiveSide.Name} cannot end a turn while {activeSide.Name} is active.");

    AdvanceTurn();
  }

  private void EndBattle(BattleOutcome outcome)
  {
    if (Phase == BattlePhase.Ended)
      return;

    _scheduler.ClearActiveFactionAvailability();
    _scheduler.ClearTurnQueue();
    Outcome = Some(outcome);
    Phase = BattlePhase.Ended;

    RaiseEvents(new SessionEndedBattleEvent(outcome));
  }

  // Objective/operation-driven outcome decision at turn end: one of the two
  // intentional places the battle can terminate. The other is the immediate-defeat
  // branch in HandleUnitDeath, which ends the battle the moment the player faction is
  // wiped (deliberate shipped behavior: a player wipe ends immediately rather than
  // waiting for turn end). This method only runs the turn-end operation outcome rules.
  // Returns true iff it ended the battle.
  private bool TryEndBattleIfDecided()
  {
    if (Phase != BattlePhase.InProgress)
      return false;

    return PlayerFaction.Match(
      Some: player =>
      {
        Operation op = _operations[player];
        if (op.Status == OperationStatus.Failed)
        {
          EndBattle(BattleOutcome.Defeat);
          return true;
        }

        if (op.Status == OperationStatus.Completed)
        {
          EndBattle(BattleOutcome.Victory);
          return true;
        }

        return false;
      },
      None: () =>
      {
        if (!_scheduler.GlobalFactionTurnOrder.Any(HasLivingUnits))
        {
          EndBattle(BattleOutcome.Draw);
          return true;
        }

        return false;
      });
  }

  internal Option<BattleBoardState.ValidatedPoint> GetUnitPosition(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return Board.FindOccupantPosition(unit.Id);
  }

  // Total for alive units: an alive session unit is always board-indexed (see UnitProofs).
  // Callers holding a liveness fact use this instead of handling an impossible None; a
  // None here means the trusted core is broken, so it throws.
  internal BattleBoardState.ValidatedPoint RequireUnitPosition(BattleUnitState unit)
    => GetUnitPosition(unit).Match(
      point => point,
      () => throw new InvalidOperationException($"Unit {unit.Id} is not indexed on the board."));

  internal Option<BattleUnitState> GetUnitAt(BattleBoardState.ValidatedPoint point)
  {
    return Board.GetOccupant(point).Bind(id =>
      id < _units.Count && _units[id].IsAlive ? Some(_units[id]) : None);
  }

  internal void MoveUnit(BattleUnitState unit, BattleBoardState.ValidatedPoint source,
    BattleBoardState.ValidatedPoint destination)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (unit.IsDead)
      throw new InvalidOperationException($"Cannot move unit {unit.Id} because it is dead.");
    var boardPosition = Board.FindOccupantPosition(unit.Id);
    if (boardPosition.IsNone)
      throw new InvalidOperationException($"Unit {unit.Id} is not tracked in the session position index.");
    if (boardPosition.Value() != source)
      throw new InvalidOperationException(
        $"Unit {unit.Id} is indexed at {boardPosition.Value().Raw}, not {source.Raw}.");
    if (!Board.TryMoveOccupant(source, destination, unit.Id))
      throw new InvalidOperationException($"Could not move unit {unit.Id} from {source.Raw} to {destination.Raw}.");
    MarkVisibilityAffected(unit);

    RaiseEvents(
      new UnitMovedBattleEvent(unit, destination, source),
      new TileOccupiedBattleEvent(unit, destination));
  }

  internal bool CanUnitActNow(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Trying to check unit while the battle is not in progress.");

    return unit.Side == ActiveSide && _scheduler.IsUnitAvailable(unit) && unit.CanAct();
  }

  internal static int GetGridDistance(Vector3I source, Vector3I destination)
  {
    return BattleBoardState.GetGridDistance(source, destination);
  }

  internal IHitChanceCalculator HitChanceCalculator => _hitChanceCalculator;

  internal int RollPercent()
  {
    return _random.Next(100);
  }

  internal bool HasLivingUnits(Faction side)
  {
    return GetFactionAliveUnits(side).Any();
  }

  // Records a unit whose own board cell changed (spawned/moved/removed) so the next dispatch
  // can scope its visibility recompute to the affected units instead of the whole pool. A
  // pending full refresh (battle start) still takes priority and clears this set.
  private void MarkVisibilityAffected(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    _visibilityAffectedUnits.Add(unit);
  }

  // Forces a full clear-and-recompute of all faction visibility on the next event dispatch.
  // Call this after any runtime mutation to a tile's BlocksLineOfSight or
  // BlocksVerticalLineOfSight (e.g. destructible terrain) — those flag changes are NOT
  // occupancy events and are not otherwise caught by the incremental refresh machinery.
  internal void InvalidateVisibility()
  {
    _visibilityFullRefreshPending = true;
  }

  // TODO: It may be nicer if this event raising is entirely handled by BattleSession
  internal void RaiseEvents(params BattleEvent[] events)
  {
    foreach (var battleEvent in events)
    {
      ArgumentNullException.ThrowIfNull(battleEvent);
      _eventDispatchQueue.Enqueue(battleEvent);
    }

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

        BattleEventCommitted?.Invoke(battleEvent);
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
    IReadOnlyList<(BattleUnitState Observer, BattleUnitState Target)> spottedDelta;
    if (_visibilityFullRefreshPending)
    {
      spottedDelta = _visibility.RefreshAllUnits(Board, _units, AliveUnits);
      _visibilityFullRefreshPending = false;
      _visibilityAffectedUnits.Clear();
    }
    else if (_visibilityAffectedUnits.Count > 0)
    {
      spottedDelta = _visibility.RefreshAffected(Board, _units, AliveUnits, _visibilityAffectedUnits);
      _visibilityAffectedUnits.Clear();
    }
    else
    {
      spottedDelta = [];
    }

    foreach (var (observer, target) in spottedDelta)
      if (observer.RecordFirstSpotting(target))
        _eventDispatchQueue.Enqueue(new UnitSpottedBattleEvent(observer, target));
  }

  private void EnqueueFactionInGlobalOrder(Faction side)
  {
    if (_scheduler.RegisterFaction(side))
      _operations[side] = new Operation(side);
  }

  private void HandleFactionLoss(Faction side)
  {
    if (HasLivingUnits(side))
      return;

    _scheduler.OnFactionEliminated(side, Phase == BattlePhase.InProgress && ActiveSide == side);
  }

  internal void AddObjective(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);

    if (!_operations.TryGetValue(faction, out Operation? op))
    {
      op = new Operation(faction);
      _operations[faction] = op;
      _scheduler.RegisterFaction(faction);
    }

    op!.AddObjective(objective);
    RaiseEvents(new ObjectiveAddedBattleEvent(faction, objective));
  }

  internal void EvaluateOperationAtTurnEnd(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    if (Phase != BattlePhase.InProgress)
      return;
    if (!_operations.TryGetValue(faction, out Operation? found))
      return;

    Operation op = found!;
    while (op.Status == OperationStatus.Active)
    {
      Option<Objective> currentOption = op.Current;
      if (currentOption.IsNone)
        break;
      Objective current =
        currentOption.Match(c => c, () => throw new InvalidOperationException("Current objective vanished."));

      if (current.IsFailed(this))
      {
        op.FailCurrent();
        RaiseEvents(new ObjectiveFailedBattleEvent(faction, current), new OperationFailedBattleEvent(faction));
        break;
      }

      if (current.IsComplete(this))
      {
        op.CompleteCurrent();
        RaiseEvents(new ObjectiveCompletedBattleEvent(faction, current));
        if (op.Status == OperationStatus.Completed)
          RaiseEvents(new OperationCompletedBattleEvent(faction));
        continue;
      }

      break;
    }
  }

  // Evaluate EVERY faction's operation at a turn end, in the deterministic
  // GlobalFactionTurnOrder, not just the faction whose turn just ended. An operation can
  // become complete/failed because of ANOTHER faction's turn (e.g. an overwatch/mine
  // reaction killing the last enemy during the enemy's turn completes the player's
  // eliminate-all objective), so per-faction evaluation would resolve it a full round late
  // (or never, if the owner cannot take another turn). EvaluateOperationAtTurnEnd guards
  // Phase == InProgress, only advances an Active operation, and is idempotent for an
  // already-resolved one, so evaluating all factions every turn end is safe and any
  // re-evaluation is a no-op.
  internal void EvaluateAllOperationsAtTurnEnd()
  {
    foreach (Faction faction in _scheduler.GlobalFactionTurnOrder)
      EvaluateOperationAtTurnEnd(faction);
  }

  internal Option<Operation> GetOperation(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    return _operations.TryGetValue(faction, out Operation? op) ? Some(op!) : None;
  }

  private void StartNextRound()
  {
    TurnNumber++;
    _scheduler.ClearSidesActedThisRound();
    _scheduler.RebuildRoundQueueFromLivingSides();

    if (_scheduler.RoundQueueCount == 0)
      throw new InvalidOperationException(
        "StartNextRound reached with no living factions; TryEndBattleIfDecided ends the battle at the prior turn end.");

    BeginNextQueuedSideTurn();
  }

  private void BeginNextQueuedSideTurn()
  {
    var nextSide = _scheduler.AdvanceActiveSideToQueueHead();

    // Refresh availability immediately after the active side flips: no observer may see the
    // previous side's units as still available.
    _scheduler.RefreshActiveFactionAvailability();

    // Turn-start hooks (buffs) fire inside this dispatch, BEFORE the AP refresh below reads
    // buffed MaxActionPoints — every side's turn start re-evaluates ALL alive units so
    // conditions that changed during another faction's turn are fresh.
    RaiseEvents(
      new ActiveSideChangedBattleEvent(nextSide),
      new TurnStartedBattleEvent(nextSide, TurnNumber));

    foreach (var unit in GetFactionAliveUnits(nextSide))
      unit.RefreshForNewTurn();
  }

  internal FactionBattleSummary GetFactionSummary(Faction faction)
  {
    return new FactionBattleSummary()
    {
      Faction = faction,
      Outcome = Outcome.Value(),
      CombatantsDead = (SysColGeneric.HashSet<Combatant>)
        [.. DeadUnits.Where(unit => unit.Side == faction).Select(unit => unit.Combatant)],
      CombatantsWounded = (SysColGeneric.HashSet<Combatant>)
      [
        ..AliveUnits.Where(unit => unit.Side == faction).Where(unit => unit.MaxHealth > unit.CurrentHealth)
          .Select(unit => unit.Combatant)
      ],
      DefeatedPerCombatant = _killsByUnit.Where(unitKilled => unitKilled.Key.Side == faction)
        .Select(unitKilled =>
          (unitKilled.Key.Combatant, unitKilled.Value.Select(killed => killed.Combatant).ToList())).ToDictionary(),
      TurnCount = TurnNumber
    };
  }
}