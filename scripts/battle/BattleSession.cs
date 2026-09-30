using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public enum BattlePhase
{
  Setup,
  InProgress,
  Ended,
}

public sealed class BattleSession
{
  public const int DefaultMovementStepActionPointCost = 1;
  public const int DefaultAttackActionPointCost = 1;
  public const int DefaultReloadActionPointCost = 1;
  public const int DefaultUseItemActionPointCost = 1;

  private readonly TurnScheduler _scheduler;
  private readonly Queue<BattleEvent> _eventDispatchQueue = [];
  private bool _isDispatchingEvents;
  private IReadOnlyList<Combatant> _capturedEnemies = [];

  // Phase-independent tactical storage; the session layers scheduling, phase/outcome, and
  // event dispatch on top of it.
  internal BattleState State { get; }

  public BattleBoardState Board => State.Board;
  internal UnitActionCache ActionOptions { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction ActiveSide => _scheduler.ActiveSide;
  public Option<Faction> PlayerFaction => State.PlayerFaction;
  public Option<BattleOutcome> Outcome { get; private set; }
  public IEnumerable<BattleUnitState> AliveUnits => State.AliveUnits;
  public IEnumerable<BattleUnitState> DeadUnits => State.DeadUnits;
  public IEnumerable<BattleObjectState> Objects => State.Objects;

  public Option<AliveUnit> TryGetAlive(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return State.TryGetAlive(unit);
  }

  public Option<LiveObject> TryGetAliveObject(BattleObjectState obj)
  {
    ArgumentNullException.ThrowIfNull(obj);
    return State.TryGetAliveObject(obj);
  }

  // Mint door for attack-target proofs: Some iff the entity is currently a targetable member
  // of THIS session — a live unit, or a live object carrying health. The proof is a snapshot
  // (membership + position at mint) that may go stale across an executor commit.
  public Option<AttackTarget> TryGetAttackTarget(BattleEntity entity)
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

  internal AliveUnit MintAlive(BattleUnitState unit) => State.MintAlive(unit);

  internal LiveObject MintAliveObject(BattleObjectState obj) => State.MintAliveObject(obj);

  internal DeadUnit MintDead(BattleUnitState unit) => State.MintDead(unit);

  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _scheduler.GlobalFactionTurnOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _scheduler.TurnQueue;

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

    State = new BattleState(board, globalFactionOrder, hitChanceCalculator, randomSeed, playerFaction);
    _scheduler = new TurnScheduler(State, State.HasConsciousUnits, State.GetFactionConsciousUnits);
    ActionOptions = new UnitActionCache(this);

    if (GlobalFactionTurnOrder.Count == 0)
      throw new ArgumentException("Battle session requires at least one faction in the global order.");

    PlayerFaction.IfSome(EnqueueFactionInGlobalOrder);

    _scheduler.InitializeQueueFromGlobalOrder();
  }

  internal IEnumerable<BattleUnitState> GetFactionAliveUnits(Faction side) => State.GetFactionAliveUnits(side);

  internal IEnumerable<BattleUnitState> GetFactionConsciousUnits(Faction side) => State.GetFactionConsciousUnits(side);

  internal bool HasConsciousUnits(Faction side) => State.HasConsciousUnits(side);

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetFactionVisibleTiles(Faction side)
    => State.GetFactionVisibleTiles(side);

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetFactionExploredTiles(Faction side)
    => State.GetFactionExploredTiles(side);

  internal bool IsUnitVisibleToFaction(Faction side, BattleUnitState target)
    => State.IsUnitVisibleToFaction(side, target);

  internal bool IsTileVisibleToFaction(Faction side, BattleBoardState.ValidatedPoint tile)
    => State.IsTileVisibleToFaction(side, tile);

  internal bool IsUnitStillAvailableThisTurn(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _scheduler.IsUnitAvailable(unit);
  }

  internal void StartBattle()
  {
    if (Phase != BattlePhase.Setup)
      throw new InvalidOperationException("Battle session can only be started from setup.");

    _scheduler.RebuildRoundQueueFromConsciousSides();
    if (_scheduler.RoundQueueCount == 0)
      throw new InvalidOperationException("Cannot start a battle without at least one conscious faction in the session.");

    foreach (var side in _scheduler.GlobalFactionTurnOrder)
    {
      if (GetObjectives(side).Count == 0)
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
    State.InvalidateVisibility();

    // Turn-start hooks (buffs) fire inside this dispatch, BEFORE the AP refresh below reads
    // buffed MaxActionPoints. Mid-dispatch observers see pre-refresh action points.
    RaiseEvents(
      new SessionStartedBattleEvent(),
      new TurnStartedBattleEvent(ActiveSide, TurnNumber));

    // A turn-start objective flip (e.g. SurviveUntilTurn with TargetTurn 1) can end the
    // battle inside that dispatch; skip the AP refresh for a dead battle.
    if (Phase == BattlePhase.Ended)
      return;

    foreach (var unit in AliveUnits)
      unit.RefreshForNewTurn();
  }

  internal BattleUnitState AddUnit(
    Combatant combatant,
    BattleBoardState.ValidatedPoint position,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor,
    IReadOnlyList<StatMod>? statMods = null)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    if (Phase == BattlePhase.Ended)
      throw new InvalidOperationException("Cannot add units after the battle has ended.");

    var unit = State.AddUnit(combatant, position, equippedWeapon, equippedArmor, statMods);
    EnqueueFactionInGlobalOrder(unit.Side);

    if (Phase == BattlePhase.Setup)
      _scheduler.EnqueueSideIfAbsent(unit.Side);
    if (Phase == BattlePhase.InProgress)
      _scheduler.AddSpawnedUnit(unit);

    RaiseEvents(new UnitAddedBattleEvent(unit, position));

    return unit;
  }

  // Trusted core: the factory pre-validates occupancy; a miss here is a caller bug.
  internal void AddObject(BattleSpecialObjectData data, BattleBoardState.ValidatedPoint position)
  {
    var state = State.AddObject(data, position);
    RaiseEvents(new ObjectPlacedBattleEvent(state, position));
  }

  internal void ApplyDamageTo(BattleUnitState unit, int amount, DamageKind kind = DamageKind.Health)
    => ApplyDamageTo(unit, [new Damage(amount, Element.Kinetic, Kind: kind)], None);

  // Object-side damage: health packets reduce the health capability, stun/status payloads are
  // ignored, and reaching zero is the terminal Destroyed transition (occupancy cleared before
  // the destruction event broadcasts). Damage is a trusted-core mutation like the unit path.
  internal void ApplyDamageTo(BattleObjectState obj, IReadOnlyList<Damage> bundle,
    Option<BattleUnitState> cause)
  {
    ArgumentNullException.ThrowIfNull(obj);
    ArgumentNullException.ThrowIfNull(bundle);
    if (TryGetAliveObject(obj).IsNone)
      throw new InvalidOperationException($"Object {obj.Id} is not live in this session.");
    var health = obj.FindCapability<ObjectHealthCapability>().Match(
      Some: value => value,
      None: () => throw new InvalidOperationException($"Object {obj.Id} has no health."));
    int amount = DamageResolver.Resolve(bundle, None).HealthDamage;
    if (amount == 0) return;
    health.Reduce(amount);
    if (health.CurrentHealth == 0)
    {
      var position = MarkObjectTerminal(obj, ObjectStatus.Destroyed);
      RaiseEvents(new ObjectDestroyedBattleEvent(obj, position, cause));
      return;
    }
    RaiseEvents(new ObjectDamagedBattleEvent(obj, bundle, amount, cause));
  }

  internal void ApplyDamageTo(BattleUnitState unit, IReadOnlyList<Damage> bundle, Option<BattleUnitState> cause)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    if (unit.IsDead)
      throw new InvalidOperationException($"Cannot damage unit {unit.Id} because it is already dead.");
    var unitPoint = Board.FindOccupantPosition(unit.Id);
    if (unitPoint.IsNone)
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
    bool wasUnconscious = unit.IsUnconscious;
    unit.ReceiveDamage(resolution.HealthDamage);
    unit.ReceiveStun(resolution.StunDamage);

    if (unit.IsDead)
    {
      HandleUnitDeath(unit, cause);
      return;
    }

    if (unit.IsUnconscious && !wasUnconscious)
    {
      var unitSide = unit.Side;
      State.MarkVisibilityAffected(unit);
      _scheduler.TryConsumeAvailableUnit(unit);
      RaiseEvents(new UnitUnconsciousBattleEvent(unit, unitPoint.Value(), cause));
      HandleFactionLoss(unitSide);
      return;
    }

    RaiseEvents(new UnitDamagedBattleEvent(unit, cause, bundle,
      resolution.ArmorDamage, resolution.HealthDamage, resolution.StunDamage));

    ApplyStatusEffectsFrom(unit, bundle, packetResolutions);
  }

  private void ApplyStatusEffectsFrom(
    BattleUnitState unit,
    IReadOnlyList<Damage> bundle,
    IReadOnlyList<DamageResolution> packetResolutions)
  {
    foreach ((Damage damage, DamageResolution resolution) in bundle.AsValueEnumerable().Zip(packetResolutions))
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
    State.MarkVisibilityAffected(unit);

    _scheduler.TryConsumeAvailableUnit(unit);
    killedBy.IfSome(killer => State.RecordKill(killer, unit));

    RaiseEvents(new UnitKilledBattleEvent(unit, unitPoint, killedBy));

    HandleFactionLoss(unitSide);
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

    if (!GetFactionAliveUnits(activeSide).AsValueEnumerable().Any(CanUnitActNow))
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

    // With no player faction there is no backstop: if every faction is wiped, Draw.
    if (!_scheduler.GlobalFactionTurnOrder.AsValueEnumerable().Any(HasConsciousUnits))
    {
      EndBattle(BattleOutcome.Draw);
      return;
    }

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

  internal void EndBattle(BattleOutcome outcome)
  {
    if (Phase == BattlePhase.Ended)
      return;

    var captured = new SysColGeneric.HashSet<Combatant>(
      System.Collections.Generic.ReferenceEqualityComparer.Instance);
    if (outcome == BattleOutcome.Victory)
      PlayerFaction.IfSome(player =>
      {
        foreach (var unit in AliveUnits)
          if (unit.Side != player && unit.IsUnconscious)
            captured.Add(unit.Combatant);
      });
    _capturedEnemies = System.Array.AsReadOnly(captured.AsValueEnumerable().ToArray());

    _scheduler.ClearActiveFactionAvailability();
    _scheduler.ClearTurnQueue();
    Outcome = Some(outcome);
    Phase = BattlePhase.Ended;

    RaiseEvents(new SessionEndedBattleEvent(outcome));
  }

  internal Option<BattleBoardState.ValidatedPoint> GetUnitPosition(BattleUnitState unit)
    => State.GetUnitPosition(unit);

  internal Option<BattleUnitState> GetUnitAt(BattleBoardState.ValidatedPoint point)
    => State.GetUnitAt(point);

  internal void MarkObjectInteracted(BattleObjectState obj)
  {
    MarkObjectTerminal(obj, ObjectStatus.Interacted);
  }

  internal (BattleObjectState Object, BattleBoardState.ValidatedPoint Position) MarkObjectExpired(
    BattleObjectState obj)
  {
    BattleBoardState.ValidatedPoint position = MarkObjectTerminal(obj, ObjectStatus.Expired);
    return (obj, position);
  }

  // Shared mark-and-clear for every terminal object transition: rejects already-terminal
  // objects, requires a board position, clears occupancy, then flips the status. The
  // captured position is what terminal events carry.
  private BattleBoardState.ValidatedPoint MarkObjectTerminal(BattleObjectState obj, ObjectStatus status)
  {
    if (obj.Status.IsSome)
      throw new InvalidOperationException($"Object {obj.Id} is not live on the board.");
    BattleBoardState.ValidatedPoint position = Board.FindObjectPosition(obj.Id).Match(
      Some: point => point,
      None: () => throw new InvalidOperationException($"Object {obj.Id} is placed but not board-indexed."));
    if (!Board.TryClearObjectOccupant(position, obj.Id))
      throw new InvalidOperationException($"Could not clear occupancy for object {obj.Id}.");
    obj.Status = Some(status);
    return position;
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
    State.MarkVisibilityAffected(unit);

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

  internal static double GetGridDistance(Vector3I source, Vector3I destination)
  {
    return BattleBoardState.GetGridDistance(source, destination);
  }

  internal IHitChanceCalculator HitChanceCalculator => State.HitChanceCalculator;

  internal int RollPercent() => State.RollPercent();

  internal bool HasLivingUnits(Faction side) => State.HasLivingUnits(side);

  // Forces a full clear-and-recompute of all faction visibility on the next event dispatch.
  // Call this after any runtime mutation to a tile's BlocksLineOfSight or
  // BlocksVerticalLineOfSight (e.g. destructible terrain) — those flag changes are NOT
  // occupancy events and are not otherwise caught by the incremental refresh machinery.
  internal void InvalidateVisibility() => State.InvalidateVisibility();

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

        ActionOptions.Invalidate(battleEvent);
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
    foreach (var (observer, target) in State.RefreshVisibility())
      _eventDispatchQueue.Enqueue(new UnitSpottedBattleEvent(observer, target));
  }

  private void EnqueueFactionInGlobalOrder(Faction side)
  {
    _scheduler.RegisterFaction(side);
  }

  private void HandleFactionLoss(Faction side)
  {
    if (HasConsciousUnits(side))
      return;

    _scheduler.OnFactionEliminated(side, Phase == BattlePhase.InProgress && ActiveSide == side);
    if (Phase == BattlePhase.InProgress
        && PlayerFaction.Match(player => player == side, () => false))
      EndBattle(BattleOutcome.Defeat);
  }

  internal void AddObjective(Faction faction, Objective objective)
  {
    if (State.AddObjective(faction, objective))
      EnqueueFactionInGlobalOrder(faction);

    RaiseEvents(new ObjectiveAddedBattleEvent(faction, objective));
  }

  // Objective-system doors: flip state + raise the flip event. Trusted core — called only
  // by ObjectiveSystem for an Ongoing objective it owns the routing of.
  internal void RecordObjectiveCompleted(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    if (objective.State != ObjectiveResult.Ongoing)
      throw new InvalidOperationException($"Objective {objective.Data.Name} is already {objective.State}.");

    objective.State = ObjectiveResult.Passed;
    RaiseEvents(new ObjectiveCompletedBattleEvent(faction, objective));
  }

  internal void RecordObjectiveFailed(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    if (objective.State != ObjectiveResult.Ongoing)
      throw new InvalidOperationException($"Objective {objective.Data.Name} is already {objective.State}.");

    objective.State = ObjectiveResult.Failed;
    RaiseEvents(new ObjectiveFailedBattleEvent(faction, objective));
  }

  internal IReadOnlyList<Objective> GetObjectives(Faction faction) => State.GetObjectives(faction);

  private void StartNextRound()
  {
    TurnNumber++;
    _scheduler.ClearSidesActedThisRound();
    _scheduler.RebuildRoundQueueFromConsciousSides();

    if (_scheduler.RoundQueueCount == 0)
      throw new InvalidOperationException(
        "StartNextRound reached with no conscious factions; AdvanceTurn should end the battle at the prior turn end.");

    BeginNextQueuedSideTurn();
  }

  private void BeginNextQueuedSideTurn()
  {
    if (_scheduler.RoundQueueCount == 0)
      throw new InvalidOperationException("Cannot begin a turn with an empty conscious-faction queue.");

    var nextSide = _scheduler.AdvanceActiveSideToQueueHead();
    if (!HasConsciousUnits(nextSide))
      throw new InvalidOperationException("Cannot begin a turn for a faction without conscious units.");

    // Refresh availability immediately after the active side flips: no observer may see the
    // previous side's units as still available.
    _scheduler.RefreshActiveFactionAvailability();

    // Turn-start hooks (buffs) fire inside this dispatch, BEFORE the AP refresh below reads
    // buffed MaxActionPoints — every side's turn start re-evaluates ALL alive units so
    // conditions that changed during another faction's turn are fresh.
    RaiseEvents(
      new ActiveSideChangedBattleEvent(nextSide),
      new TurnStartedBattleEvent(nextSide, TurnNumber));

    // A turn-start objective flip (e.g. SurviveUntilTurn reaching its target) can end the
    // battle inside that dispatch; skip the AP refresh for a dead battle.
    if (Phase == BattlePhase.Ended)
      return;

    foreach (var unit in GetFactionAliveUnits(nextSide))
      unit.RefreshForNewTurn();
  }

  internal FactionBattleSummary GetFactionSummary(Faction faction)
  {
    return new FactionBattleSummary()
    {
      Faction = faction,
      Outcome = Outcome.Value(),
      CapturedEnemies = Outcome == Some(BattleOutcome.Victory) && PlayerFaction == Some(faction)
        ? _capturedEnemies
        : [],
      CombatantsPresent = (SysColGeneric.HashSet<Combatant>)
      [
        .. AliveUnits.AsValueEnumerable().Where(unit => unit.Side == faction).Select(unit => unit.Combatant).ToArray(),
        .. DeadUnits.AsValueEnumerable().Where(unit => unit.Side == faction).Select(unit => unit.Combatant).ToArray(),
      ],
      CombatantsDead = (SysColGeneric.HashSet<Combatant>)
        [.. DeadUnits.AsValueEnumerable().Where(unit => unit.Side == faction).Select(unit => unit.Combatant).ToArray()],
      CombatantsWounded = (SysColGeneric.HashSet<Combatant>)
      [
        ..AliveUnits.AsValueEnumerable().Where(unit => unit.Side == faction).Where(unit => unit.MaxHealth > unit.CurrentHealth)
          .Select(unit => unit.Combatant).ToArray()
      ],
      DefeatedPerCombatant = State.KillsByUnit.AsValueEnumerable().Where(unitKilled => unitKilled.Key.Side == faction)
        .Select(unitKilled =>
          (unitKilled.Key.Combatant, unitKilled.Value.AsValueEnumerable().Select(killed => killed.Combatant).ToList()))
        .ToDictionary(entry => entry.Item1, entry => entry.Item2),
      TurnCount = TurnNumber,
      HealthByCombatant = BuildHealthByCombatant(faction),
    };
  }

  // Health reports for the faction's living participants — unconscious included. Dead
  // participants stay represented by the present/dead sets and the campaign skips them
  // when consuming returns.
  private Dictionary<Combatant, BattleHealthSummary> BuildHealthByCombatant(Faction faction)
  {
    var health = new Dictionary<Combatant, BattleHealthSummary>();

    foreach (BattleUnitState unit in GetFactionAliveUnits(faction))
    {
      health[unit.Combatant] = new BattleHealthSummary(unit.MaxHealth, unit.TotalHealthDamageTaken);
    }

    return health;
  }
}