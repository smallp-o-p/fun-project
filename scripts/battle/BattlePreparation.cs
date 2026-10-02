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

  internal BattleState State { get; }

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
    State.Committed += RoutePreparationObjectives;
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
    unit.EvaluateBuffs(_readContext);
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

  // Initial objective insertion shares the objective-owned addition routine (registration
  // plus the ObjectiveAdded notification).
  internal void AddObjective(Faction faction, Objective objective)
    => ObjectiveHistory.AddFollowUp(State, faction, objective);

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

    // The complete initial pool tops up to its effective maximum AP (post spawn-buff
    // evaluation) — dead companions included: their spawn-active grants raise the maximum
    // after the constructor initialized current AP, and they remain pool identities.
    foreach (BattleUnitState unit in State.Units)
      unit.RefreshForNewTurn();
    State.InvalidateVisibility();

    State.Committed -= RoutePreparationObjectives;
    return Right<BattleSetupFailure, BattleState>(State);
  }

  // Preparation objectives evaluate against the placement stream so placement-counting
  // objectives (e.g. bomb defusal tallies) never miss an initial object. Flips record and
  // raise through the shared history operations; an end directive has no receiver here —
  // preparation cannot end a battle that does not exist yet — so only history is kept and
  // only follow-up queueing applies.
  private void RoutePreparationObjectives(BattleEvent battleEvent)
  {
    foreach ((Faction owner, Objective objective) in ObjectiveHistory.SnapshotCandidates(State, battleEvent))
    {
      ObjectiveResult result = objective.Check(owner, battleEvent, _readContext);
      if (result == ObjectiveResult.Ongoing)
        continue;

      ObjectiveHistory.RecordFlip(State, owner, objective, result == ObjectiveResult.Passed);
      ObjectiveDirectiveData? directive = result == ObjectiveResult.Passed
        ? objective.Data.OnComplete
        : objective.Data.OnFail;
      if (directive is QueueDirectiveData queue)
        foreach (ObjectiveData followUp in queue.FollowUps)
          AddObjective(owner, followUp.Instantiate());
    }
  }
}
