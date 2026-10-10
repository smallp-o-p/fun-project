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

// The owning test fixture for battle tests: every fixture is built through the production
// BattleFactory, so a successfully constructed fixture owns one factory-started
// BattleSession/BattleRuntime pair, the runtime owns the session's single executor, and a
// fixture-owned declared recording system captures every committed event — including all
// startup events — behind an explicit assertion window (ClearEvents starts a new one;
// nothing clears it implicitly). Spawn is always a reinforcement; UnitAt returns the native
// BattleUnitState; Alive/Live/At mint current proofs through the runtime on each call. The
// Solo/Duel/Started/UiBattle scenario factories build authored BattleSetup data directly
// and the action conveniences are thin wrappers that submit once through those same doors.
// Presentation objects are retained too: Ui mints one controller bound to the same runtime,
// and OwnNode/AttachDirector transfer fresh Godot nodes to the fixture so disposal frees
// them alongside the runtime.
public sealed class BattleFixture : IDisposable
{
  private readonly List<BattleEvent> _events = [];
  private bool _disposed;
  private readonly bool _runEnemyTurns;

  // Captured by the fixture recorder from the first startup event; a successfully
  // constructed fixture always recorded at least one, so this is never null afterwards.
  public BattleSession Session { get; }
  public BattleRuntime Runtime { get; }
  public BattleBoardState Board => Session.Board;
  public IReadOnlyList<Faction> Factions { get; }
  public IReadOnlyList<BattleEvent> Events => _events;

  private readonly Option<BattleUnitState> _playerUnit;
  private readonly Option<BattleUnitState> _enemyUnit;
  private readonly Option<BattleUnitState> _supportUnit;

  public Faction PlayerFaction => Factions[0];
  public Faction EnemyFaction => Factions[1];
  public BattleUnitState PlayerUnit => _playerUnit.RequireSome("This fixture has no player-unit role; use its Spawn result.");
  public BattleUnitState EnemyUnit => _enemyUnit.RequireSome("This fixture has no enemy-unit role; use its Spawn result.");
  public BattleUnitState Unit => PlayerUnit;
  public BattleUnitState SupportUnit => _supportUnit.RequireSome("This fixture has no support-unit role; only UiBattle provides one.");

  public BattleFixture(BattleSetup setup, IHitChanceCalculator hitChanceCalculator = null, bool runEnemyTurns = false)
  {
    ArgumentNullException.ThrowIfNull(setup);
    _runEnemyTurns = runEnemyTurns;
    Factions = setup.Sides.AsValueEnumerable().Select(side => side.Faction).ToArray();
    var recorder = new FixtureRecordingSystem(_events);
    Runtime = BattleFactory
      .Start(setup with { Systems = [.. setup.Systems, recorder] }, hitChanceCalculator)
      .Match(
        Right: started => started,
        Left: failure => throw new InvalidOperationException(
          $"Fixture setup failed ({failure.Reason}): {failure.Message}"));
    Session = recorder.Session.RequireSome("A factory-started fixture must have recorded a startup event.");

    // Player/enemy/support roles come from unit-added records matched to the final setup's
    // authored placements by combatant identity and authored placement point (never live
    // position/liveness or the raw event order): a startup hook spawning a reinforcement
    // cannot steal an authored role. Raw units are retained even if they die or move during
    // startup.
    List<BattleUnitState> authored = [];
    foreach (BattleSideSetup side in setup.Sides)
      foreach (UnitPlacement placement in side.Units)
        authored.Add(recorder.AddedUnits.TryGetValue(
          (placement.Loadout.Combatant, placement.Position), out BattleUnitState added)
          ? added
          : throw new InvalidOperationException(
            $"No unit-added record matched authored placement {placement.Loadout.Combatant.Name} at {placement.Position}."));
    if (authored.Count > 0)
    {
      BattleUnitState player = authored[0];
      _playerUnit = Some(player);
      foreach (BattleUnitState candidate in authored.AsValueEnumerable().Skip(1))
      {
        if (ReferenceEquals(candidate.Side, player.Side))
        {
          if (_supportUnit.IsNone)
            _supportUnit = Some(candidate);
        }
        else if (_enemyUnit.IsNone)
        {
          _enemyUnit = Some(candidate);
        }
      }
    }
  }

  // Simulations control both sides through one ordinary submission. AI integration tests
  // choose the external boundary instead; both paths use the runtime's sole executor.
  // Invalid or stale actions reach that executor unchanged.
  public BattleActionExecResult Submit(BattleAction action)
    => _runEnemyTurns ? Runtime.ExecuteAction(action) : Runtime.ExecuteSingleAction(action);

  public T Query<T>(IBattleSessionQuery<T> query) => Runtime.Query(query);

  public AliveUnit Alive(BattleUnitState unit) => Runtime.TryGetAlive(unit).RequireSome();

  public LiveObject Live(BattleObjectState obj) => Runtime.TryGetAliveObject(obj).RequireSome();

  public AttackTarget Target(BattleUnitState unit) =>
    Runtime.TryGetAttackTarget(new BattleEntity.Unit(unit)).RequireSome();

  public AttackTarget Target(BattleObjectState obj) =>
    Runtime.TryGetAttackTarget(new BattleEntity.BoardObject(obj)).RequireSome();

  public BattleBoardState.ValidatedPoint At(Vector3I position) => Runtime.TryGetTile(position).RequireSome();

  public BattleBoardState.ValidatedPoint At(int x, int y, int z) => At(new Vector3I(x, y, z));

  public BattleUnitState UnitAt(Vector3I position) => Query(new GetUnitAtTile(At(position))).RequireSome();

  public AliveUnit SingleAliveUnit(Faction faction)
    => Query(new GetFactionAliveUnits(faction)).AsValueEnumerable().Single();

  // Gameplay reinforcement: submits through the runtime like any other action.
  public BattleUnitState Spawn(Combatant combatant, Vector3I position,
    Option<Weapon> weapon = default, Option<ItemWith<ArmorCapability>> armor = default,
    IReadOnlyList<StatMod>? statMods = null)
  {
    Submit(new SpawnUnit(combatant, At(position), weapon, armor, statMods));
    return UnitAt(position);
  }

  // Progression integration setup: awards the first step's cost, commits the path, and
  // unlocks that step on the supplied combatant's own progression object. Call before
  // factory construction so the deployed unit already carries the unlocked effects.
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
    int health = 20, int actionPoints = 4, string unitName = "Alpha",
    Func<BattleSetup, BattleSetup> configure = null)
  {
    var faction = TestData.MakeFaction("Player");
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(dimensions),
      [new BattleSideSetup(faction, DefaultSideObjectives(None, faction),
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(unitName, faction,
          health: health, actionPoints: actionPoints)), unitPosition)])],
      Seed: 7);
    return new BattleFixture(configure?.Invoke(setup) ?? setup);
  }

  // The old Start() objective default: EliminateAllOpposingForces on every side, and only
  // the designated player's copy carries the Victory directive.
  private static IReadOnlyList<ObjectiveData> DefaultSideObjectives(Option<Faction> playerFaction, Faction side)
  {
    var data = new EliminateAllOpposingForcesObjectiveData();
    if (playerFaction == Some(side))
      data.OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory };
    return [data];
  }

  private static UnitLoadout SideLoadout(Faction faction, UnitSpec side) =>
    new(TestData.MakeCombatant(side.Name, faction, health: side.Health,
      actionPoints: side.ActionPoints, aim: side.Aim, buffs: side.Buffs),
      side.Weapon, side.Armor);

  // A started Player-vs-Enemy duel with one unit per side. The authored map argument wins
  // over dimensions; positions default to the (4,0,1)/(4,0,4) convention.
  public static BattleFixture Duel(UnitSpec player = null, UnitSpec enemy = null,
    Vector3I? dimensions = null, BattleMapData map = null,
    IHitChanceCalculator hitChanceCalculator = null, int? randomSeed = null,
    bool playerControlled = false, Func<BattleSetup, BattleSetup> configure = null, bool runEnemyTurns = false)
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    UnitSpec playerSide = player ?? new UnitSpec("Alpha");
    UnitSpec enemySide = enemy ?? new UnitSpec("Hostile");
    Option<Faction> designatedPlayer = playerControlled ? Some(playerFaction) : None;
    var setup = new BattleSetup(
      map ?? TestData.MakeOpenBattleMap(dimensions ?? new Vector3I(8, 1, 8)),
      [
        new BattleSideSetup(playerFaction, DefaultSideObjectives(designatedPlayer, playerFaction),
          [new UnitPlacement(SideLoadout(playerFaction, playerSide), playerSide.Position ?? new Vector3I(4, 0, 1))]),
        new BattleSideSetup(enemyFaction, DefaultSideObjectives(designatedPlayer, enemyFaction),
          [new UnitPlacement(SideLoadout(enemyFaction, enemySide), enemySide.Position ?? new Vector3I(4, 0, 4))]),
      ],
      Seed: randomSeed ?? 7,
      PlayerFaction: designatedPlayer);
    return new BattleFixture(configure?.Invoke(setup) ?? setup, hitChanceCalculator, runEnemyTurns);
  }

  // The presentation scenario: the standard duel (8x1x8, Hero at (4,0,1) with a damage-10
  // rifle, Goon at (4,0,4) with 10 health) plus a surviving Support at (7,0,7), AlwaysHit
  // rolls, and the player configured as the battle's player faction. UI and director wiring
  // stays lazy: tests reach it through Ui and AttachDirector.
  public static BattleFixture UiBattle() =>
    Duel(
      player: new("Hero", Weapon: TestData.MakeWeapon("Rifle", damage: 10)),
      enemy: new("Goon", Health: 10),
      hitChanceCalculator: new AlwaysHitCalculator(), playerControlled: true,
      configure: setup => setup with
      {
        Sides =
        [
          setup.Sides[0] with { Units = [.. setup.Sides[0].Units,
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Support", setup.Sides[0].Faction)),
              new Vector3I(7, 0, 7))] },
          setup.Sides[1],
        ],
      });

  // A started battle whose factions derive from the placements' own combatants in
  // first-appearance order, one inert FakeObjective per faction, armor-inclusive spawning.
  public static BattleFixture Started(Vector3I dimensions, params UnitPlacement[] placements) =>
    Started(TestData.MakeOpenBattleMap(dimensions), placements);

  public static BattleFixture Started(BattleMapData map, params UnitPlacement[] placements)
  {
    Faction[] factions = placements.AsValueEnumerable()
      .Select(placement => placement.Loadout.Combatant.OwningFaction).Distinct().ToArray();
    BattleSideSetup[] sides = factions.AsValueEnumerable().Select(faction =>
      new BattleSideSetup(faction, [new FakeObjectiveData()],
        placements.AsValueEnumerable()
          .Where(placement => ReferenceEquals(placement.Loadout.Combatant.OwningFaction, faction))
          .ToArray())).ToArray();
    return new BattleFixture(new BattleSetup(map, sides, Seed: 7));
  }

  // Explicitly starts a new assertion window; never called by Submit or conveniences.
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
    Submit(BattleAction.AttackEntity(Alive(attacker), Target(target)));

  public BattleActionExecResult Attack(BattleUnitState attacker, BattleObjectState target) =>
    Submit(BattleAction.AttackEntity(Alive(attacker), Target(target)));

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

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0) where TEventKey : IBattleEventTag =>
    Runtime.RegisterHook<TEventKey>(hook, priority);

  public bool UnregisterHook<TEventKey>(BattleHook hook) where TEventKey : IBattleEventTag =>
    Runtime.UnregisterHook<TEventKey>(hook);

  // ---- Presentation ownership ---------------------------------------------------------

  // Fake playback-busy flag shared with the fixture's UI controller; tests flip it directly.
  public bool Busy { get; set; }

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
  // returning it so local test doubles keep their type. The fixture owns the subscription.
  public T AttachDirector<T>(T director) where T : EventPlaybackDirector
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(director);
    if (!_directors.Contains(director))
    {
      OwnNode(director);
      Runtime.ActionCompleted += director.EnqueueResult;
      _directors.Add(director);
    }
    return director;
  }

  // Disposes the UI, frees owned nodes in reverse registration order, then closes the
  // runtime — all under one idempotent flag set at entry. Events and the raw Session/Board
  // handles stay inspectable afterwards for lifecycle tests.
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
        foreach (EventPlaybackDirector director in _directors)
          Runtime.ActionCompleted -= director.EnqueueResult;
        for (int i = _ownedNodes.Count - 1; i >= 0; i--)
          if (Godot.GodotObject.IsInstanceValid(_ownedNodes[i]))
            _ownedNodes[i].Free();
        _ownedNodes.Clear();
        _directors.Clear();
      }
    }
    finally
    {
      Runtime.Dispose();
    }
  }

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
