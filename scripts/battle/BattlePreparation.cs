using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Factory-owned preparation: accepts the parsed layout and authored dependencies, places
// initial units and objects through the shared state operations, owns initial objective
// setup, and finishes with the consciousness/AP/visibility bookkeeping the scheduler and
// runtime assume. Builder operations are internal construction operations, not gameplay
// actions: no scheduler, no active faction, no hook registry, no terminal directives.
// Initial placement notifications commit through the state's shared event dispatcher so
// preparation-added objectives observe them; declared systems register only after Complete,
// so they never see a replay of this stream.
internal sealed class BattlePreparation
{
  private readonly BattleReadContext _readContext;
  private readonly Action<BattleEvent> _routeObjectives;
  private readonly Action<BattleEvent> _forwardPrepared;

  internal BattleState State { get; }

  // The preparation event feed: placement/objective notifications observed before the
  // runtime exists. Detached by Complete so subscribers never see battle events twice.
  internal event Action<BattleEvent>? Prepared;

  internal BattlePreparation(
    BattleBoardState board,
    IEnumerable<Faction> factions,
    IHitChanceCalculator? hitChanceCalculator = null,
    int? randomSeed = null,
    Option<Faction> playerFaction = default)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(factions);

    State = new BattleState(board, factions, hitChanceCalculator, randomSeed, playerFaction);
    _readContext = new BattleReadContext(State, None, None, None);
    _routeObjectives = RoutePreparationObjectives;
    _forwardPrepared = forward => Prepared?.Invoke(forward);
    State.Committed += _routeObjectives;
    State.Committed += _forwardPrepared;
  }

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
    State.RaiseEvents(new UnitAddedBattleEvent(unit, position));
    // The runtime path evaluates spawn buffs inside the UnitAdded dispatch (executor
    // default hook); preparation owns that bookkeeping itself, committing the same order.
    unit.EvaluateBuffs(_readContext, ReconcileBuffFlip);
    ReconcileParticipant(unit);
    return unit;
  }

  // Trusted core: the caller pre-validates occupancy; a miss here is a caller bug.
  internal void AddObject(BattleSpecialObjectData data, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(data);
    BattleObjectState state = State.AddObject(data, position);
    State.RaiseEvents(new ObjectPlacedBattleEvent(state, position));
  }

  // Shared initialization reconciliation, used by AddUnit and the fixture's preset path:
  // a dead participant leaves the board so its tile is reusable (already absent is valid
  // after prior initialization), an unconscious body stays occupied, and every identity
  // remains in the pool. Only disabled participants need the immediate visibility pass —
  // clearing their stale caches without fabricating gameplay events. A conscious spawn
  // returns before it: its placement stream's first-spots stay on their dispatches, and
  // a later buff-discovered spot must not be consumed here ahead of the opening refresh.
  internal void ReconcileParticipant(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (unit.IsDead)
      State.Board.FindOccupantPosition(unit.Id).Match(
        Some: point =>
        {
          if (!State.Board.TryClearOccupant(point, unit.Id))
            throw new InvalidOperationException($"Could not clear unit {unit.Id} from {point.Raw}.");
        },
        None: () => { });
    if (unit.IsAlive && !unit.IsUnconscious)
      return;
    State.MarkVisibilityAffected(unit);
    State.RefreshVisibility();
  }

  // Per-grant buff bookkeeping at the initial-placement mutation boundary: the running
  // owner's visibility marking without scheduler or outcome work (a spawn's stun is zero,
  // so the shared clamp cannot knock the unit out here). The buff event's own dispatch
  // resolves the mark and queues genuine first spots before broadcasting — nothing is
  // consumed silently.
  private void ReconcileBuffFlip(BattleUnitState unit, bool wasConscious)
  {
    ArgumentNullException.ThrowIfNull(unit);
    State.MarkVisibilityAffected(unit);
  }

  internal void AddObjective(Faction faction, Objective objective)
  {
    ArgumentNullException.ThrowIfNull(faction);
    ArgumentNullException.ThrowIfNull(objective);
    if (State.AddObjective(faction, objective))
      State.RegisterFaction(faction);

    State.RaiseEvents(new ObjectiveAddedBattleEvent(faction, objective));
  }

  // Final bookkeeping: every side must keep at least one living, conscious unit; every
  // initial unit tops up to its effective maximum AP (post spawn-buff evaluation); the
  // first battle dispatch performs the authoritative visibility pass. A failed check
  // abandons preparation with a typed failure naming the offending faction.
  internal Either<BattleSetupFailure, BattleState> Complete()
  {
    if (State.Factions.Count == 0)
      throw new ArgumentException("Battle preparation requires at least one faction.");

    foreach (Faction faction in State.Factions)
    {
      if (!State.HasConsciousUnits(faction))
        return Left<BattleSetupFailure, BattleState>(new BattleSetupFailure(
          BattleSetupFailureReason.NoConsciousUnits,
          $"Faction {faction.Name} has no living, conscious units."));
    }

    foreach (BattleUnitState unit in State.AliveUnits)
      unit.RefreshForNewTurn();
    State.InvalidateVisibility();

    State.Committed -= _routeObjectives;
    State.Committed -= _forwardPrepared;
    return Right<BattleSetupFailure, BattleState>(State);
  }

  // Preparation objectives evaluate against the placement stream so placement-counting
  // objectives (e.g. bomb defusal tallies) never miss an initial object. Flips record and
  // raise through the same state; an end directive has no receiver here — preparation
  // cannot end a battle that does not exist yet — so only history is kept.
  private void RoutePreparationObjectives(BattleEvent battleEvent)
  {
    foreach ((Faction owner, Objective objective) in ObjectiveSystem.SnapshotCandidates(State, battleEvent))
    {
      ObjectiveResult result = objective.Check(owner, battleEvent, _readContext);
      switch (result)
      {
        case ObjectiveResult.Ongoing:
          continue;
        case ObjectiveResult.Passed:
          RecordFlip(owner, objective, passed: true);
          ApplyPreparationDirective(owner, objective.Data.OnComplete);
          break;
        default:
          RecordFlip(owner, objective, passed: false);
          ApplyPreparationDirective(owner, objective.Data.OnFail);
          break;
      }
    }
  }

  private void RecordFlip(Faction owner, Objective objective, bool passed)
  {
    if (objective.State != ObjectiveResult.Ongoing)
      throw new InvalidOperationException($"Objective {objective.Data.Name} is already {objective.State}.");
    objective.State = passed ? ObjectiveResult.Passed : ObjectiveResult.Failed;
    State.RaiseEvents(passed
      ? new ObjectiveCompletedBattleEvent(owner, objective)
      : new ObjectiveFailedBattleEvent(owner, objective));
  }

  private void ApplyPreparationDirective(Faction owner, ObjectiveDirectiveData? directive)
  {
    if (directive is QueueDirectiveData queue)
      foreach (ObjectiveData followUp in queue.FollowUps)
        AddObjective(owner, followUp.Instantiate());
  }
}
