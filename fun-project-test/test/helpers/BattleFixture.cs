using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using ZLinq;

namespace FunProject.Tests;

// The owning test fixture for battle tests: one BattleSession/BattleRuntime pair per fixture
// instance, the runtime owns the session's single executor, and the fixture records every
// committed event behind an explicit assertion window (ClearEvents starts a new one; nothing
// clears it implicitly). Spawn/UnitAt return the native BattleUnitState; Alive/Live/At mint
// current proofs through the runtime on each call. Scenario factories and action conveniences
// are deliberately out of scope here.
public sealed class BattleFixture : IDisposable
{
  private readonly List<BattleEvent> _events = [];
  private bool _disposed;

  public BattleSession Session { get; }
  public BattleRuntime Runtime { get; }
  public BattleBoardState Board => Session.Board;
  public IReadOnlyList<Faction> Factions { get; }
  public IReadOnlyList<BattleEvent> Events => _events;

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

  // Detaches recording first so a close-time event cannot re-enter the window, then closes
  // the runtime (which owns and disposes the session's only executor). Idempotent; Events and
  // the raw Session/Board handles stay inspectable afterwards for lifecycle tests.
  public void Dispose()
  {
    if (_disposed)
      return;
    Runtime.BattleEventCommitted -= RecordEvent;
    Runtime.Dispose();
    _disposed = true;
  }

  private void RecordEvent(BattleEvent value) => _events.Add(value);

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
