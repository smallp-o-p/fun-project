using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Progression;
using FunProject.Weapons;
using ZLinq;

namespace FunProject.Tests;

// The owning test fixture for battle tests: one BattleSession/BattleRuntime pair per fixture
// instance, the runtime owns the session's single executor, and the fixture records every
// committed event behind an explicit assertion window (ClearEvents starts a new one; nothing
// clears it implicitly). Spawn/UnitAt return the native BattleUnitState; Alive/Live/At mint
// current proofs through the runtime on each call. The Solo/Duel/Started/UiBattle scenario
// factories and the action conveniences are thin wrappers that submit once through those same
// doors. Presentation objects are retained too: Ui mints one controller bound to the same
// runtime, and OwnNode/AttachDirector transfer fresh Godot nodes to the fixture so disposal
// frees them alongside the runtime.
public sealed class BattleFixture : IDisposable
{
  private readonly List<BattleEvent> _events = [];
  private bool _disposed;

  public BattleSession Session { get; }
  public BattleRuntime Runtime { get; }
  public BattleBoardState Board => Session.Board;
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
    // instances must reach both the fixture and the session.
    Factions = factions.AsValueEnumerable().ToArray();
    Session = new BattleSession(board, Factions, hitChanceCalculator, randomSeed, playerFaction);
    Runtime = new BattleRuntime(Session);
    Runtime.BattleEventCommitted += RecordEvent;
  }

  // Raw submission door: deliberately invalid or stale actions reach the executor unchanged,
  // so tests keep exercising rejection, interruption, and stale-proof paths.
  public BattleActionExecResult Submit(BattleAction action) => Runtime.ExecuteAction(action);

  public T Query<T>(IBattleSessionQuery<T> query) => Runtime.Query(query);

  public AliveUnit Alive(BattleUnitState unit) => Runtime.TryGetAlive(unit).RequireSome();

  public LiveObject Live(BattleObjectState obj) => Runtime.TryGetAliveObject(obj).RequireSome();

  public BattleBoardState.ValidatedPoint At(Vector3I position) => Runtime.TryGetTile(position).RequireSome();

  public BattleBoardState.ValidatedPoint At(int x, int y, int z) => At(new Vector3I(x, y, z));

  public BattleUnitState UnitAt(Vector3I position) => Query(new GetUnitAtTile(At(position))).RequireSome();

  public AliveUnit SingleAliveUnit(Faction faction)
    => Query(new GetFactionAliveUnits(faction)).AsValueEnumerable().Single();

  public BattleUnitState Spawn(Combatant combatant, Vector3I position,
    Option<Weapon> weapon = default, Option<ItemWith<ArmorCapability>> armor = default)
  {
    Submit(new SpawnUnit(combatant, At(position), weapon, armor));
    return UnitAt(position);
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
        battle.Session.AddObjective(faction, new FakeObjective());
      foreach (UnitPlacement placement in placements)
        battle.Spawn(placement.Loadout.Combatant, placement.Position,
          placement.Loadout.Weapon, placement.Loadout.Armor);
      battle.Start();
      return battle;
    }
    catch
    {
      battle.Dispose();
      throw;
    }
  }

  // Setup convenience preserving the old missing-objective policy: every faction without an
  // authored objective gets EliminateAllOpposingForces, and only the configured player's
  // copy carries the Victory directive. Tests of missing objectives submit the raw
  // BattleAction.StartBattle() through Submit instead.
  public BattleActionExecResult Start()
  {
    ThrowIfDisposed();
    foreach (Faction faction in Session.GlobalFactionTurnOrder)
    {
      if (Session.GetObjectives(faction).Count > 0)
        continue;
      var data = new EliminateAllOpposingForcesObjectiveData();
      if (Session.PlayerFaction == Some(faction))
        data.OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory };
      Session.AddObjective(faction, data.Instantiate());
    }
    return Submit(BattleAction.StartBattle());
  }

  // Explicitly starts a new assertion window; never called by Start, Submit, or conveniences.
  public void ClearEvents()
  {
    ThrowIfDisposed();
    _events.Clear();
  }

  // ---- Action conveniences ------------------------------------------------------------
  // Each mints fresh proofs at call time and submits exactly once. They never clear the
  // recorded event window, and invalid/stale routes still surface through Submit unchanged.

  public BattleActionExecResult Move(BattleUnitState unit, Vector3I[] destinations,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost) =>
    Submit(BattleAction.MoveUnit(Alive(unit), destinations.AsValueEnumerable().Select(position => At(position)).ToArray(), actionPointCost));

  public BattleActionExecResult Attack(BattleUnitState attacker, BattleUnitState target) =>
    Submit(BattleAction.AttackUnit(Alive(attacker), Alive(target)));

  public BattleActionExecResult ApplyDamage(BattleUnitState unit, int amount, DamageKind kind = DamageKind.Health) =>
    Submit(BattleAction.ApplyDamage(Alive(unit), amount, kind));

  public BattleActionExecResult Pass(BattleUnitState unit) =>
    Submit(BattleAction.PassUnit(Alive(unit)));

  public BattleActionExecResult EndFactionTurn(Faction faction) =>
    Submit(BattleAction.EndFactionTurn(faction));

  public BattleActionExecResult AdvanceTurn() =>
    EndFactionTurn(Query(new GetActiveSideQuery()));

  public BattleActionExecResult Throw(BattleUnitState unit, ItemWith<ThrowableCapability> item, Vector3I target) =>
    Submit(BattleAction.ThrowItem(Alive(unit), item, At(target)));

  public BattleActionExecResult Use(BattleUnitState unit, ItemWith<ChargesCapability> item) =>
    Submit(BattleAction.UseItem(Alive(unit), item));

  public BattleActionExecResult Reload(BattleUnitState unit, AmmunitionedWeapon weapon) =>
    Submit(BattleAction.ReloadWeapon(Alive(unit), weapon));

  public BattleActionExecResult Interact(BattleUnitState unit, BattleObjectState obj) =>
    Submit(BattleAction.InteractWithObject(Alive(unit), Live(obj)));

  // ---- Hook forwarding ----------------------------------------------------------------

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0) where TEventKey : BattleEventTag =>
    Runtime.RegisterHook<TEventKey>(hook, priority);

  public bool UnregisterHook<TEventKey>(BattleHook hook) where TEventKey : BattleEventTag =>
    Runtime.UnregisterHook<TEventKey>(hook);

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
  // then closes the runtime — all under one idempotent flag set at entry. Events and the raw
  // Session/Board handles stay inspectable afterwards for lifecycle tests. The runtime and
  // any bound directors share this lifetime because Bind exposes no unbind.
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
      Runtime.BattleEventCommitted -= RecordEvent;
      Runtime.Dispose();
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
