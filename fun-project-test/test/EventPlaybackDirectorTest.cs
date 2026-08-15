using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class EventPlaybackDirectorTest
{
  [TestCase(TestName = "Queued events play in commit order; Busy toggles; idle fires once")]
  public void EventsPlayInOrderAndIdleFiresOnce()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    using var runtime = new BattleRuntime(session);

    int idleCount = 0;
    var immediate = new ImmediateDirector();
    immediate.PlaybackIdle += () => idleCount++;

    immediate.Bind(runtime);
    Assert.False(immediate.Busy);

    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction), session.Board.At(1, 0, 1)));

    Assert.True(immediate.Busy); // queued but not yet ticked
    immediate.Tick();
    Assert.False(immediate.Busy);
    Assert.Equal(1, immediate.Played.Count);
    Assert.True(immediate.Played[0] is UnitAddedBattleEvent);
    Assert.Equal(1, idleCount);
    immediate.Tick(); // idle fires only once
    Assert.Equal(1, idleCount);
  }

  [TestCase(TestName = "Consecutive move steps coalesce into one path step; tile bookkeeping folds in")]
  public void ConsecutiveMoveStepsCoalesceIntoOnePathStep()
  {
    using MoveFixture fixture = new(new Vector3I(5, 1, 5));
    var director = new CaptureDirector();
    int idleCount = 0;
    director.PlaybackIdle += () => idleCount++;
    director.Bind(fixture.Runtime);
    director.RegisterUnitMesh(
      fixture.Unit.State,
      new Node3D { Position = BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)) });

    fixture.MoveAlong([new Vector3I(2, 0, 1), new Vector3I(3, 0, 1)]);

    director.Tick(); // the whole two-step path plays as ONE step
    Assert.Equal(1, director.MoveRuns.Count);
    IReadOnlyList<Vector3I> run = director.MoveRuns[0];
    Assert.Equal(2, run.Count);
    Assert.Equal(new Vector3I(2, 0, 1), run[0]);
    Assert.Equal(new Vector3I(3, 0, 1), run[1]);
    Assert.Equal(0, director.Played.Count); // TileOccupied bookkeeping never surfaces as its own step
    Assert.False(director.Busy);
    Assert.Equal(1, idleCount);
  }

  [TestCase(TestName = "An interrupting event breaks the path into separate runs")]
  public void InterruptingEventBreaksThePathIntoSeparateRuns()
  {
    using MoveFixture fixture = new(new Vector3I(5, 1, 5));
    var director = new CaptureDirector();
    director.Bind(fixture.Runtime);

    // After the first step commits, an interrupt damages the mover mid-walk (it survives).
    fixture.Runtime.RegisterHook<UnitMovedBattleEvent>(
      new DamageAfterFirstStepHook(new Vector3I(2, 0, 1), () =>
        BattleAction.ApplyDamage(fixture.Runtime.TryGetAlive(fixture.Unit.State).RequireSome(), 2)));

    fixture.MoveAlong([new Vector3I(2, 0, 1), new Vector3I(3, 0, 1)]);

    director.Tick(); // run 1: only the first step, the interrupt breaks the path
    Assert.Equal(1, director.MoveRuns.Count);
    Assert.Equal(1, director.MoveRuns[0].Count);
    Assert.Equal(new Vector3I(2, 0, 1), director.MoveRuns[0][0]);

    director.Tick(); // the interrupt's damage plays between the runs
    Assert.Equal(1, director.Played.Count);
    Assert.True(director.Played[0] is UnitDamagedBattleEvent);

    director.Tick(); // run 2: the remaining step continues as a new path
    Assert.Equal(2, director.MoveRuns.Count);
    Assert.Equal(1, director.MoveRuns[1].Count);
    Assert.Equal(new Vector3I(3, 0, 1), director.MoveRuns[1][0]);
    Assert.False(director.Busy);
  }

  [TestCase(TestName = "Move steps for hidden (dead) meshes complete instantly without moving")]
  public void MoveStepsForHiddenMeshesCompleteInstantlyWithoutMoving()
  {
    using MoveFixture fixture = new(new Vector3I(5, 1, 5));
    var director = new BareDirector();
    var mesh = new Node3D { Position = BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)) };
    director.RegisterUnitMesh(fixture.Unit.State, mesh);
    mesh.Visible = false; // killed units hide their mesh; their pending steps must not tween

    var run = new List<UnitMovedBattleEvent>
    {
      new(fixture.Unit.State, fixture.Runtime.TryGetTile(new Vector3I(2, 0, 1)).RequireSome(),
        fixture.Runtime.TryGetTile(new Vector3I(1, 0, 1)).RequireSome()),
    };
    bool done = false;
    director.MoveStepPublic(run, () => done = true);

    Assert.True(done); // synchronous completion: no tween started
    Assert.Equal(BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)), mesh.Position);
  }

  // Runtime-first single-faction battle with one spawned, movable unit at (1,0,1).
  private sealed class MoveFixture : IDisposable
  {
    public BattleRuntime Runtime { get; }
    public BattleTestUnit Unit { get; }

    public MoveFixture(Vector3I dimensions)
    {
      var faction = BattleTestFactory.MakeFaction("Player");
      var session = BattleTestFactory.MakeSession(dimensions, [faction]);
      Runtime = new BattleRuntime(session);
      Unit = BattleActionTestHelper.SpawnUnit(
        Runtime,
        BattleTestFactory.MakeCombatant("Mover", faction),
        new Vector3I(1, 0, 1));
      BattleActionTestHelper.EnsureEveryFactionHasObjective(session);
      BattleActionTestHelper.StartBattle(Runtime);
    }

    public void MoveAlong(params Vector3I[] path)
    {
      AliveUnit mover = Runtime.TryGetAlive(Unit.State).RequireSome();
      Runtime.ExecuteAction(BattleAction.MoveUnit(mover, path.Select(tile =>
        Runtime.TryGetTile(tile).RequireSome()).ToList()));
    }

    public void Dispose() => Runtime.Dispose();
  }

  // Interrupts with a damage action once the mover reaches the trigger tile.
  private sealed class DamageAfterFirstStepHook(Vector3I triggerPosition, Func<BattleAction> build)
    : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => evt.Position.Raw == triggerPosition ? [build()] : [];
  }

  private sealed partial class ImmediateDirector : EventPlaybackDirector
  {
    public List<BattleEvent> Played { get; } = [];

    protected override void PlayStep(BattleEvent battleEvent, Action done)
    {
      Played.Add(battleEvent);
      done();
    }
  }

  // Captures both single events and coalesced move runs; every step completes immediately.
  private sealed partial class CaptureDirector : EventPlaybackDirector
  {
    public List<BattleEvent> Played { get; } = [];
    public List<IReadOnlyList<Vector3I>> MoveRuns { get; } = [];

    protected override void PlayStep(BattleEvent battleEvent, Action done)
    {
      Played.Add(battleEvent);
      done();
    }

    protected override void PlayMoveStep(IReadOnlyList<UnitMovedBattleEvent> run, Action done)
    {
      MoveRuns.Add(run.Select(step => step.Position.Raw).ToList());
      done();
    }
  }

  // No overrides: exposes the production step methods directly.
  private sealed partial class BareDirector : EventPlaybackDirector
  {
    public void MoveStepPublic(IReadOnlyList<UnitMovedBattleEvent> run, Action done) => PlayMoveStep(run, done);
  }
}
