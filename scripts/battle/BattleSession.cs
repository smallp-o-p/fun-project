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

// The running receiver: owns the fully constructed scheduler and every operation that
// requires it — activations, turns, reinforcements, and terminal requests (which delegate
// to the executor's current step). Constructed only by the runtime owner from a prepared
// state and a valid scheduler; no StartBattle, no phase, no optional outcome, no setup
// queue. Gameplay writes run only inside the executor's current-step scope, so the receiver
// is never handed out as a nominally active object that goes stale after completion.
public sealed class BattleSession
{
  public const int DefaultMovementStepActionPointCost = 1;
  public const int DefaultAttackActionPointCost = 1;
  public const int DefaultReloadActionPointCost = 1;
  public const int DefaultUseItemActionPointCost = 1;

  private readonly TurnScheduler _scheduler;
  private BattleStep? _step;

  internal BattleState State { get; }

  internal UnitActionCache ActionOptions => State.ActionOptions;

  public Option<Faction> PlayerFaction => State.PlayerFaction;

  internal BattleSession(BattleState state, TurnScheduler scheduler)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(scheduler);
    State = state;
    _scheduler = scheduler;
    State.Committed += OnStateEventCommitted;
  }

  internal event Action<BattleEvent> BattleEventCommitted = delegate { };

  private void OnStateEventCommitted(BattleEvent battleEvent)
  {
    ActionOptions.Invalidate(battleEvent);
    BattleEventCommitted.Invoke(battleEvent);
  }

  internal BattleTurn CurrentTurn => _scheduler.CurrentTurn;
  internal Faction ActiveFaction => _scheduler.ActiveSide;
  internal int RoundNumber => _scheduler.RoundNumber;

  internal Option<AliveUnit> TryGetAlive(BattleUnitState unit) => State.TryGetAlive(unit);

  internal Option<LiveObject> TryGetAliveObject(BattleObjectState obj) => State.TryGetAliveObject(obj);

  internal Option<AttackTarget> TryGetAttackTarget(BattleEntity entity) => State.TryGetAttackTarget(entity);

  // ---- Execution-step scope -------------------------------------------------------------

  // The executor opens its step per submission; terminal requests delegate to it, and turn
  // advancement consults it so a pending outcome never starts a new transition.
  internal void BeginStep(BattleStep step)
  {
    ArgumentNullException.ThrowIfNull(step);
    _step = step;
  }

  internal void EndStep() => _step = null;

  internal bool HasPendingOutcome => _step is not null && _step.HasPendingOutcome;

  internal void RequestEnd(BattleOutcome outcome)
  {
    if (_step is null)
      throw new InvalidOperationException("A terminal request requires an open execution step.");
    _step.RequestEnd(outcome);
  }

  // ---- Reinforcements and turn flow -----------------------------------------------------

  // In-progress reinforcement: initial placement belongs to preparation; this door is the
  // submission path (SpawnUnit) for units joining a running battle.
  internal BattleUnitState AddUnit(
    Combatant combatant,
    BattleBoardState.ValidatedPoint position,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor,
    IReadOnlyList<StatMod>? statMods = null)
  {
    ArgumentNullException.ThrowIfNull(combatant);

    State.RegisterFaction(combatant.OwningFaction);
    BattleUnitState unit = State.AddUnit(combatant, position, equippedWeapon, equippedArmor, statMods);
    _scheduler.RegisterReinforcement(unit);

    State.RaiseEvents(new UnitAddedBattleEvent(unit, position));

    return unit;
  }

  // Opening session/turn dispatch through the same event machinery as later turns. The
  // full visibility recompute picks up board authoring (tile BlocksLineOfSight) finalized
  // after the last spawn; the all-unit AP refresh tops every initial unit up after the
  // opening-turn buff pass, so the factory returns with full effective AP everywhere.
  internal void OpeningTurnDispatch()
  {
    State.InvalidateVisibility();

    BattleTurn opening = _scheduler.CurrentTurn;
    State.RaiseEvents(
      new SessionStartedBattleEvent(),
      new TurnStartedBattleEvent(opening.ActiveFaction, opening.RoundNumber));

    // A terminal opening-turn objective skips only the refresh; its buff/AP work finished
    // in the dispatch above before the executor settled.
    if (HasPendingOutcome)
      return;

    foreach (BattleUnitState unit in State.AliveUnits)
      unit.RefreshForNewTurn();
  }

  internal void EndUnitActivation(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    Faction activeSide = ActiveFaction;
    if (unit.Side != activeSide)
      throw new InvalidOperationException($"Unit {unit.Id} is not on the active side.");

    BattleBoardState.ValidatedPoint unitPoint = State.GetUnitPosition(unit).Match(
      Some: point => point,
      None: () => throw new InvalidOperationException(
        $"Cannot end activation for unit {unit.Id} because it is not on the board."));

    if (!_scheduler.ConsumeActivation(unit))
      throw new InvalidOperationException($"Unit {unit.Id} is not available this turn.");

    State.RaiseEvents(new UnitActivationEndedBattleEvent(unit, unitPoint));

    // A terminal directive inside the activation-ended dispatch owns the ending; do not
    // begin the auto-advance transition on its behalf.
    if (HasPendingOutcome)
      return;

    if (!State.GetFactionAliveUnits(activeSide).AsValueEnumerable().Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  internal void EndFactionTurn(Faction expectedActiveSide)
  {
    Faction activeSide = ActiveFaction;
    if (activeSide != expectedActiveSide)
      throw new InvalidOperationException(
        $"{expectedActiveSide.Name} cannot end a turn while {activeSide.Name} is active.");

    // A pending outcome drops the transition: passing a unit after a terminal request must
    // not open the next faction's turn (the guard re-runs after the turn-end dispatch).
    if (HasPendingOutcome)
      return;

    AdvanceTurn();
  }

  private void AdvanceTurn()
  {
    BattleTurn outgoing = _scheduler.CurrentTurn;
    State.RaiseEvents(new TurnEndedBattleEvent(outgoing.ActiveFaction, outgoing.RoundNumber));

    // A turn-end directive or player wipe during the dispatch above ends the battle;
    // bail before opening another faction turn or round.
    if (HasPendingOutcome)
      return;

    Option<BattleTurn> next = _scheduler.AdvanceTurn();
    if (next.IsNone)
    {
      // With no player faction there is no backstop: if no conscious forces remain, Draw.
      RequestEnd(BattleOutcome.Draw);
      return;
    }

    BattleTurn started = next.RequireSome();
    State.RaiseEvents(
      new ActiveSideChangedBattleEvent(started.ActiveFaction),
      new TurnStartedBattleEvent(started.ActiveFaction, started.RoundNumber));

    // A turn-start objective flip (e.g. SurviveUntilTurn reaching its target) can request
    // the ending inside that dispatch; skip the AP refresh for the settled battle.
    if (HasPendingOutcome)
      return;

    foreach (BattleUnitState unit in State.GetFactionAliveUnits(started.ActiveFaction))
      unit.RefreshForNewTurn();
  }

  // ---- Damage, status, and object pipelines ---------------------------------------------

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
      State.RaiseEvents(new ObjectDestroyedBattleEvent(obj, position, cause));
      return;
    }
    State.RaiseEvents(new ObjectDamagedBattleEvent(obj, bundle, amount, cause));
  }

  internal void ApplyDamageTo(BattleUnitState unit, IReadOnlyList<Damage> bundle, Option<BattleUnitState> cause)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    if (unit.IsDead)
      throw new InvalidOperationException($"Cannot damage unit {unit.Id} because it is already dead.");
    var unitPoint = State.Board.FindOccupantPosition(unit.Id);
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
      State.MarkVisibilityAffected(unit);
      _scheduler.ConsumeActivation(unit);
      State.RaiseEvents(new UnitUnconsciousBattleEvent(unit, unitPoint.Value(), cause));
      HandleConsciousnessLoss(unit);
      return;
    }

    State.RaiseEvents(new UnitDamagedBattleEvent(unit, cause, bundle,
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
    if (spec.ApplyChancePercent < 100 && State.RollPercent() >= spec.ApplyChancePercent)
      return;

    ActiveStatusEffect applied = unit.ApplyStatusEffect(spec);
    State.RaiseEvents(new UnitStatusEffectAppliedBattleEvent(unit, spec, applied.RemainingTurns));
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

    Option<BattleBoardState.ValidatedPoint> unitPointOption = State.GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    bool occupantCleared = State.Board.TryClearOccupant(unitPoint, unit.Id);
    if (!occupantCleared)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} from {unitPoint.Raw}.");
    State.MarkVisibilityAffected(unit);

    _scheduler.ConsumeActivation(unit);
    killedBy.IfSome(killer => State.RecordKill(killer, unit));

    State.RaiseEvents(new UnitKilledBattleEvent(unit, unitPoint, killedBy));

    HandleConsciousnessLoss(unit);
  }

  // Loss-of-consciousness reconciliation at the owning mutation boundary: the scheduler
  // folds the faction loss into queues/availability, and a wiped designated player loses
  // immediately (the backstop that suppresses objective end directives).
  private void HandleConsciousnessLoss(BattleUnitState unit)
  {
    _scheduler.ReconcileConsciousness(unit);
    if (State.HasConsciousUnits(unit.Side))
      return;

    if (PlayerFaction.Match(player => player == unit.Side, () => false))
      RequestEnd(BattleOutcome.Defeat);
  }

  internal (BattleObjectState Object, BattleBoardState.ValidatedPoint Position) MarkObjectExpired(
    BattleObjectState obj)
  {
    BattleBoardState.ValidatedPoint position = MarkObjectTerminal(obj, ObjectStatus.Expired);
    return (obj, position);
  }

  internal void MarkObjectInteracted(BattleObjectState obj)
  {
    MarkObjectTerminal(obj, ObjectStatus.Interacted);
  }

  // Shared mark-and-clear for every terminal object transition: rejects already-terminal
  // objects, requires a board position, clears occupancy, then flips the status. The
  // captured position is what terminal events carry.
  private BattleBoardState.ValidatedPoint MarkObjectTerminal(BattleObjectState obj, ObjectStatus status)
  {
    if (obj.Status.IsSome)
      throw new InvalidOperationException($"Object {obj.Id} is not live on the board.");
    BattleBoardState.ValidatedPoint position = State.Board.FindObjectPosition(obj.Id).Match(
      Some: point => point,
      None: () => throw new InvalidOperationException($"Object {obj.Id} is placed but not board-indexed."));
    if (!State.Board.TryClearObjectOccupant(position, obj.Id))
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
    var boardPosition = State.Board.FindOccupantPosition(unit.Id);
    if (boardPosition.IsNone)
      throw new InvalidOperationException($"Unit {unit.Id} is not tracked in the session position index.");
    if (boardPosition.Value() != source)
      throw new InvalidOperationException(
        $"Unit {unit.Id} is indexed at {boardPosition.Value().Raw}, not {source.Raw}.");
    if (!State.Board.TryMoveOccupant(source, destination, unit.Id))
      throw new InvalidOperationException($"Could not move unit {unit.Id} from {source.Raw} to {destination.Raw}.");
    State.MarkVisibilityAffected(unit);

    State.RaiseEvents(
      new UnitMovedBattleEvent(unit, destination, source),
      new TileOccupiedBattleEvent(unit, destination));
  }

  internal bool CanUnitActNow(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return unit.Side == ActiveFaction && _scheduler.IsUnitAvailable(unit) && unit.CanAct();
  }

  internal bool IsUnitStillAvailableThisTurn(BattleUnitState unit) => _scheduler.IsUnitAvailable(unit);

  // Read context for the receiver's own trusted core work (primitives, effect resolution).
  // Valid only while the receiver is authoritative — exactly the scope where it is used.
  internal BattleReadContext RunningContext() => new(State, Some(_scheduler.CurrentTurn), None, Some(this));

  internal IHitChanceCalculator HitChanceCalculator => State.HitChanceCalculator;

  internal int RollPercent() => State.RollPercent();

  // The receiver's door to the shared event dispatcher for trusted primitives and hooks.
  internal void RaiseEvents(params BattleEvent[] events) => State.RaiseEvents(events);
}
