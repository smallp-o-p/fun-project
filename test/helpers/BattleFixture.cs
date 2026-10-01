#nullable disable warnings
using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Progression;
using FunProject.Stats;
using FunProject.Weapons;
using ZLinq;

namespace FunProject.Tests;

// The owning test fixture for battle tests: one private preparation builder until Start, then
// the single runtime (and its sole executor) that Start constructs from the completed
// preparation. Spawn/PlaceObject/AddObjective before Start are preparation operations sharing
// the factory's routines; their events commit through the shared dispatcher and the fixture
// records them from its owned builder. After Start, Spawn is a runtime reinforcement
// submission and events record from the runtime. ClearEvents starts a new assertion window;
// nothing clears it implicitly. Alive/Live/At/Target mint current proofs through the runtime
// on each call. The Solo/Duel/Started/UiBattle scenario factories and the action conveniences
// are thin wrappers that submit once through those same doors.
public sealed class BattleFixture : IDisposable
{
  private readonly List<BattleEvent> _events = [];
  private readonly BattlePreparation _preparation;
  private readonly List<Action<BattleRuntime>> _deferredRegistrations = [];
  private BattleRuntime _runtime;
  private bool _started;
  private bool _disposed;

  public BattleRuntime Runtime
  {
    get
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      if (!_started)
        throw new InvalidOperationException("This fixture has no runtime before Start.");
      return _runtime;
    }
  }

  // Trusted-core door for tests: resolves the current running receiver through the runtime's
  // lifecycle at each call (the Running value owns it persistently), so a completed battle
  // has no receiver to hand out and nothing is stored here.
  internal BattleSession Session
  {
    get
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      if (!_started)
        throw new InvalidOperationException("This fixture has no running receiver before Start.");
      return _runtime.CurrentSession;
    }
  }

  public BattleBoardState Board => _preparation.State.Board;
  public IReadOnlyList<Faction> Factions { get; }
  public IReadOnlyList<BattleEvent> Events => _events;

  private Option<BattleUnitState> _playerUnit;
  private Option<BattleUnitState> _enemyUnit;
  private Option<BattleUnitState> _supportUnit;

  public Faction PlayerFaction => Factions[0];
  public Faction EnemyFaction => Factions[1];
  public BattleUnitState PlayerUnit => _playerUnit.RequireSome("This fixture has no player-unit role; use its Spawn result.");
  public BattleUnitState EnemyUnit => _enemyUnit.RequireSome("This fixture has no enemy-unit role; use its Spawn result.");
  public BattleUnitState Unit => PlayerUnit;
  public BattleUnitState SupportUnit => _supportUnit.RequireSome("This fixture has no support-unit role; only UiBattle provides one.");

  public BattleFixture(Vector3I dimensions, IEnumerable<Faction> factions,
    IHitChanceCalculator hitChanceCalculator = null, int? randomSeed = null,
    Option<Faction> playerFaction = default)
    : this(new BattleBoardState(dimensions), factions, hitChanceCalculator, randomSeed, playerFaction) { }

  public BattleFixture(BattleBoardState board, IEnumerable<Faction> factions,
    IHitChanceCalculator hitChanceCalculator = null, int? randomSeed = null,
    Option<Faction> playerFaction = default)
  {
    // Materialize once: the caller may pass a one-shot enumerable, and the same faction
    // instances must reach both the fixture and the preparation.
    Factions = factions.AsValueEnumerable().ToArray();
    _preparation = new BattlePreparation(board, Factions, hitChanceCalculator, randomSeed, playerFaction);
    _preparation.Prepared += RecordEvent;
  }

  // Raw submission door: deliberately invalid or stale actions reach the executor unchanged,
  // so tests keep exercising rejection, interruption, and stale-proof paths. Returns None
  // when the battle already completed; conveniences unwrap the Some side.
  public Option<BattleActionExecResult> Submit(BattleAction action) => Runtime.ExecuteAction(action);

  // The read side of the current phase: preparation contexts before Start, the runtime's
  // context afterwards. Inspection queries answer in both phases; submissions need Start.
  public T Query<T>(IBattleSessionQuery<T> query)
  {
    ThrowIfDisposed();
    return _started
      ? _runtime.Query(query)
      : query.Execute(new BattleReadContext(_preparation.State, None, None, None));
  }

  // Observes committed events from the first opening dispatch onward, ordered after the
  // executor's log append and option invalidation: the handler binds to the runtime's
  // shared ordered stream, before the hook pass fires.
  internal void OnCommitted(Action<BattleEvent> handler)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(handler);
    if (!_started)
    {
      _deferredRegistrations.Add(runtime => runtime.BattleEventCommitted += handler);
      return;
    }
    _runtime.BattleEventCommitted += handler;
  }

  private BattleState ReadState => _started ? _runtime.State : _preparation.State;

  public AliveUnit Alive(BattleUnitState unit) => ReadState.TryGetAlive(unit).RequireSome();

  public LiveObject Live(BattleObjectState obj) => ReadState.TryGetAliveObject(obj).RequireSome();

  public AttackTarget Target(BattleUnitState unit) =>
    ReadState.TryGetAttackTarget(new BattleEntity.Unit(unit)).RequireSome();

  public AttackTarget Target(BattleObjectState obj) =>
    ReadState.TryGetAttackTarget(new BattleEntity.Object(obj)).RequireSome();

  public BattleBoardState.ValidatedPoint At(Vector3I position) => Board.ValidatePoint(position).RequireSome();

  public BattleBoardState.ValidatedPoint At(int x, int y, int z) => At(new Vector3I(x, y, z));

  public BattleUnitState UnitAt(Vector3I position)
    => Query(new GetUnitAtTile(At(position))).RequireSome();

  // Where a pooled unit currently stands (None once it is dead and off the board).
  public Option<BattleBoardState.ValidatedPoint> PositionOf(BattleUnitState unit)
    => Query(new GetUnitPosition(unit));

  public AliveUnit SingleAliveUnit(Faction faction)
    => Query(new GetFactionAliveUnits(faction)).AsValueEnumerable().Single();

  // Before Start: preparation placement through the factory's routine (its events commit
  // immediately and are recorded). After Start: a reinforcement submission; the returned
  // state is read back through the unit-at-tile query.
  public BattleUnitState Spawn(Combatant combatant, Vector3I position,
    Option<Weapon> weapon = default, Option<ItemWith<ArmorCapability>> armor = default,
    IReadOnlyList<StatMod>? statMods = null)
  {
    ThrowIfDisposed();
    if (!_started)
      return _preparation.AddUnit(combatant, At(position), weapon, armor, statMods);

    Submit(new SpawnUnit(combatant, At(position), weapon, armor, statMods));
    return UnitAt(position);
  }

  // Preparation-only: initial object placement is never a gameplay command.
  public BattleObjectState PlaceObject(BattleSpecialObjectData data, Vector3I position)
  {
    ThrowIfDisposed();
    if (_started)
      throw new InvalidOperationException("Initial object placement happens before Start.");
    _preparation.AddObject(data, At(position));
    return _preparation.State.Objects.AsValueEnumerable().Last();
  }

  // Preparation-only objective setup, mirroring the factory's ordering (objectives before
  // placements, so placement-counting objectives never miss an initial object).
  public void AddObjective(Faction faction, Objective objective)
  {
    ThrowIfDisposed();
    if (_started)
      throw new InvalidOperationException("Initial objectives are prepared before Start.");
    _preparation.AddObjective(faction, objective);
  }

  // Preparation presets apply through the preparation's own bookkeeping: no gameplay
  // damage/kill events are fabricated, armor does not split the packet, and dead bodies
  // leave the board while unconscious bodies stay. After Start the same call submits the
  // real damage action through the runtime.
  public void Damage(BattleUnitState unit, int amount, DamageKind kind = DamageKind.Health)
  {
    ThrowIfDisposed();
    if (amount <= 0)
      return;
    if (_started)
    {
      Submit(BattleAction.ApplyDamage(Alive(unit), amount, kind));
      return;
    }

    _preparation.ApplyDamagePreset(unit, amount, kind);
  }

  // Progression integration setup: awards the first step's cost, commits the path, and
  // unlocks that step on the supplied combatant's own progression object. Call before
  // Spawn so the spawned unit already carries the unlocked effects.
  public void UnlockFirstStep(Combatant combatant, SkillPathData path)
  {
    ThrowIfDisposed();
    combatant.Progression.AwardPoints(path.Steps[0].Cost);
    combatant.Progression.TryCommit(path);
    combatant.Progression.TryUnlockNext(path).RequireSome();
  }

  // ---- Scenario factories -------------------------------------------------------------

  // A started single-unit battle on the Player faction. PlayerUnit retains the spawn result.
  public static BattleFixture Solo(Vector3I dimensions, Vector3I unitPosition,
    int health = 20, int actionPoints = 4, string unitName = "Alpha", bool start = true)
  {
    var faction = TestData.MakeFaction("Player");
    var battle = new BattleFixture(dimensions, [faction]);
    try
    {
      battle._playerUnit = Some(battle.Spawn(TestData.MakeCombatant(unitName, faction,
        health: health, actionPoints: actionPoints), unitPosition));
      if (start)
        battle.Start();
      return battle;
    }
    catch
    {
      battle.Dispose();
      throw;
    }
  }

  private BattleUnitState SpawnSide(Faction faction, UnitSpec side, Vector3I defaultPosition) =>
    Spawn(TestData.MakeCombatant(side.Name, faction, health: side.Health,
      actionPoints: side.ActionPoints, aim: side.Aim, buffs: side.Buffs),
      side.Position ?? defaultPosition, side.Weapon, side.Armor);

  // A started Player-vs-Enemy duel with one unit per side. The board argument wins over
  // dimensions; positions default to the (4,0,1)/(4,0,4) convention.
  public static BattleFixture Duel(UnitSpec player = null, UnitSpec enemy = null,
    Vector3I? dimensions = null, BattleBoardState board = null,
    IHitChanceCalculator hitChanceCalculator = null, int? randomSeed = null,
    bool playerControlled = false, bool start = true)
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    var battle = new BattleFixture(board ?? new BattleBoardState(dimensions ?? new Vector3I(8, 1, 8)),
      [playerFaction, enemyFaction], hitChanceCalculator, randomSeed,
      playerControlled ? Some(playerFaction) : None);
    try
    {
      battle._playerUnit = Some(battle.SpawnSide(playerFaction, player ?? new UnitSpec("Alpha"), new Vector3I(4, 0, 1)));
      battle._enemyUnit = Some(battle.SpawnSide(enemyFaction, enemy ?? new UnitSpec("Hostile"), new Vector3I(4, 0, 4)));
      if (start)
        battle.Start();
      return battle;
    }
    catch
    {
      battle.Dispose();
      throw;
    }
  }

  // The presentation scenario: the standard duel (8x1x8, Hero at (4,0,1) with a damage-10
  // rifle, Goon at (4,0,4) with 10 health) plus a surviving Support at (7,0,7) spawned before
  // Start, AlwaysHit rolls, and the player configured as the battle's player faction. UI and
  // director wiring stays lazy: tests reach it through Ui and AttachDirector.
  public static BattleFixture UiBattle()
  {
    var battle = Duel(
      player: new("Hero", Weapon: TestData.MakeWeapon("Rifle", damage: 10)),
      enemy: new("Goon", Health: 10),
      hitChanceCalculator: new AlwaysHitCalculator(), playerControlled: true, start: false);
    try
    {
      battle._supportUnit = Some(battle.Spawn(
        TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(7, 0, 7)));
      battle.Start();
      return battle;
    }
    catch
    {
      battle.Dispose();
      throw;
    }
  }

  // A started battle whose factions derive from the placements' own combatants in
  // first-appearance order, one inert FakeObjective per faction, armor-inclusive spawning.
  public static BattleFixture Started(Vector3I dimensions, params UnitPlacement[] placements) =>
    Started(new BattleBoardState(dimensions), placements);

  public static BattleFixture Started(BattleBoardState board, params UnitPlacement[] placements)
  {
    Faction[] factions = placements.AsValueEnumerable()
      .Select(placement => placement.Loadout.Combatant.OwningFaction).Distinct().ToArray();
    var battle = new BattleFixture(board, factions);
    try
    {
      foreach (Faction faction in factions)
        battle.AddObjective(faction, new FakeObjective());
      foreach (UnitPlacement placement in placements)
        battle.Spawn(placement.Loadout.Combatant, placement.Position,
          placement.Loadout.Weapon, placement.Loadout.Armor, placement.Loadout.StatMods);
      battle.Start();
      return battle;
    }
    catch
    {
      battle.Dispose();
      throw;
    }
  }

  // Completes preparation, constructs the one runtime, binds hook registrations made before
  // Start, and dispatches the opening step. Preparation failures (a side with no living,
  // conscious units) throw before any runtime exists.
  public void Start()
  {
    ThrowIfDisposed();
    if (_started)
      throw new InvalidOperationException("This fixture has already started.");
    foreach (Faction faction in _preparation.State.Factions)
    {
      if (_preparation.State.GetObjectives(faction).Count > 0)
        continue;
      var data = new EliminateAllOpposingForcesObjectiveData();
      if (_preparation.State.PlayerFaction == Some(faction))
        data.OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory };
      _preparation.AddObjective(faction, data.Instantiate());
    }

    BattleState state = _preparation.Complete().Match(
      Right: prepared => prepared,
      Left: failure => throw new InvalidOperationException(failure.Message));

    _runtime = BattleRuntime.Create(state);
    _runtime.BattleEventCommitted += RecordEvent;
    foreach (Action<BattleRuntime> registration in _deferredRegistrations)
      registration(_runtime);
    _deferredRegistrations.Clear();
    _started = true;
    _runtime.DispatchOpeningTurn();
  }

  // Explicitly starts a new assertion window; never called by Start, Submit, or conveniences.
  public void ClearEvents()
  {
    ThrowIfDisposed();
    _events.Clear();
  }

  // ---- Action conveniences ------------------------------------------------------------
  // Each mints fresh proofs at call time and submits exactly once, unwrapping the Some
  // result (an accepted-but-interrupted action still returns its result here). They never
  // clear the recorded event window, and invalid/stale routes still surface unchanged.

  public BattleActionExecResult Move(BattleUnitState unit, Vector3I[] destinations,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost) =>
    Submit(BattleAction.MoveUnit(Alive(unit), destinations.AsValueEnumerable().Select(position => At(position)).ToArray(), actionPointCost)).RequireSome();

  public BattleActionExecResult Attack(BattleUnitState attacker, BattleUnitState target) =>
    Submit(BattleAction.AttackEntity(Alive(attacker), Target(target))).RequireSome();

  public BattleActionExecResult Attack(BattleUnitState attacker, BattleObjectState target) =>
    Submit(BattleAction.AttackEntity(Alive(attacker), Target(target))).RequireSome();

  public BattleActionExecResult ApplyDamage(BattleUnitState unit, int amount, DamageKind kind = DamageKind.Health) =>
    Submit(BattleAction.ApplyDamage(Alive(unit), amount, kind)).RequireSome();

  public BattleActionExecResult Pass(BattleUnitState unit) =>
    Submit(BattleAction.PassUnit(Alive(unit))).RequireSome();

  public BattleActionExecResult EndFactionTurn(Faction faction) =>
    Submit(BattleAction.EndFactionTurn(faction)).RequireSome();

  public BattleActionExecResult AdvanceTurn() =>
    EndFactionTurn(Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

  public BattleActionExecResult Throw(BattleUnitState unit, ItemWith<ThrowableCapability> item, Vector3I target) =>
    Submit(BattleAction.ThrowItem(Alive(unit), item, At(target))).RequireSome();

  public BattleActionExecResult Use(BattleUnitState unit, ItemWith<ChargesCapability> item) =>
    Submit(BattleAction.UseItem(Alive(unit), item)).RequireSome();

  public BattleActionExecResult Reload(BattleUnitState unit, AmmunitionedWeapon weapon) =>
    Submit(BattleAction.ReloadWeapon(Alive(unit), weapon)).RequireSome();

  public BattleActionExecResult Interact(BattleUnitState unit, BattleObjectState obj) =>
    Submit(BattleAction.InteractWithObject(Alive(unit), Live(obj))).RequireSome();

  // ---- Hook forwarding ----------------------------------------------------------------
  // Registrations made before Start bind when Start constructs the runtime — after complete
  // preparation, before the opening dispatch — matching declared-system timing.

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0) where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    if (!_started)
    {
      _deferredRegistrations.Add(runtime => runtime.RegisterHook<TEventKey>(hook, priority));
      return;
    }
    Runtime.RegisterHook<TEventKey>(hook, priority);
  }

  public bool UnregisterHook<TEventKey>(BattleHook hook) where TEventKey : BattleEventTag =>
    Runtime.UnregisterHook<TEventKey>(hook);

  // The current read context for boundary tests over conditions/objectives: preparation
  // before Start, the runtime's lifecycle-derived context afterwards.
  internal BattleReadContext Read => _started
    ? _runtime.GetReadContext()
    : new BattleReadContext(_preparation.State, None, None, None);

  // ---- Presentation ownership ---------------------------------------------------------

  // Fake playback-busy flag shared with the fixture's UI controller; tests flip it directly.
  public bool Busy;

  private Option<BattleUiController> _ui;
  private readonly List<Godot.Node> _ownedNodes = [];
  private readonly List<EventPlaybackDirector> _directors = [];

  // Lazily mints the fixture's single UI controller for the player faction, bound to the
  // same runtime the fixture owns and reading the Busy field. Repeated reads return the
  // same controller; only Dispose tears it down.
  public BattleUiController Ui
  {
    get
    {
      ThrowIfDisposed();
      if (_ui.IsNone)
        _ui = Some(new BattleUiController(Runtime, PlayerFaction, () => Busy));
      return _ui.RequireSome();
    }
  }

  // Transfers ownership of a FRESH test node to the fixture (freed by Dispose in reverse
  // registration order). Never pass a node an AutoFree scope or another owner still manages.
  public T OwnNode<T>(T node) where T : Godot.Node
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(node);
    if (!_ownedNodes.Contains(node))
      _ownedNodes.Add(node);
    return node;
  }

  // Owns and binds the supplied concrete director to the fixture's runtime exactly once,
  // returning it so local test doubles keep their type. EventPlaybackDirector.Bind has no
  // unbind, so the publisher and the director share the fixture's lifetime.
  public T AttachDirector<T>(T director) where T : EventPlaybackDirector
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(director);
    if (!_directors.Contains(director))
    {
      OwnNode(director);
      director.Bind(Runtime);
      _directors.Add(director);
    }
    return director;
  }

  // Detaches recording, disposes the UI, frees owned nodes in reverse registration order,
  // then closes the runtime — all under one idempotent flag set at entry. Events and the
  // board stay inspectable afterwards for lifecycle tests. The runtime and any bound
  // directors share this lifetime because Bind exposes no unbind.
  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    try
    {
      try
      {
        _ui.IfSome(ui => ui.Dispose());
      }
      finally
      {
        for (int i = _ownedNodes.Count - 1; i >= 0; i--)
          if (Godot.GodotObject.IsInstanceValid(_ownedNodes[i]))
            _ownedNodes[i].Free();
        _ownedNodes.Clear();
        _directors.Clear();
      }
    }
    finally
    {
      if (_started)
        _runtime.BattleEventCommitted -= RecordEvent;
      _runtime?.Dispose();
    }
  }

  private void RecordEvent(BattleEvent value) => _events.Add(value);

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

  // Setup-only description of one Duel side: authored values, no live simulation state.
  public sealed record UnitSpec(
    string Name,
    Vector3I? Position = null,
    int Health = 20,
    int ActionPoints = 4,
    int Aim = 65,
    Option<Weapon> Weapon = default,
    Option<ItemWith<ArmorCapability>> Armor = default,
    Buff[] Buffs = null);
}
