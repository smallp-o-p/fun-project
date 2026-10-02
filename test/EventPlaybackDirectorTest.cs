using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class EventPlaybackDirectorTest
{
  [TestCase(TestName = "Queued events play in commit order; Busy toggles; idle fires once")]
  public void EventsPlayInOrderAndIdleFiresOnce()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    battle.Spawn(TestData.MakeCombatant("Seed", faction, vision: 0), new Vector3I(0, 0, 0));
    battle.Start();
    int idleCount = 0;
    var director = battle.AttachDirector(new CaptureDirector()); // bound before the spawn
    director.PlaybackIdle += () => idleCount++;

    Assert.False(director.Busy);

    battle.Spawn(TestData.MakeCombatant("Alpha", battle.PlayerFaction, vision: 0), new Vector3I(1, 0, 1));

    Assert.True(director.Busy); // queued but not yet ticked
    director.Tick();
    Assert.False(director.Busy);
    Assert.Equal(1, director.Played.Count);
    Assert.True(director.Played[0] is UnitAddedBattleEvent);
    Assert.Equal(1, idleCount);
    director.Tick(); // idle fires only once
    Assert.Equal(1, idleCount);
  }

  [TestCase(TestName = "Consecutive move steps coalesce into one path step; tile bookkeeping folds in")]
  public void ConsecutiveMoveStepsCoalesceIntoOnePathStep()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), unitName: "Mover");
    var director = battle.AttachDirector(new CaptureDirector());
    int idleCount = 0;
    director.PlaybackIdle += () => idleCount++;
    var mesh = battle.OwnNode(new Node3D
    {
      Position = BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)),
    });
    director.RegisterUnitMesh(battle.Unit, mesh);

    battle.Move(battle.Unit, [new Vector3I(2, 0, 1), new Vector3I(3, 0, 1)]);

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
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), unitName: "Mover");
    var director = battle.AttachDirector(new CaptureDirector());

    // After the first step commits, an interrupt damages the mover mid-walk (it survives).
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageAfterFirstStepHook(new Vector3I(2, 0, 1), () =>
        BattleAction.ApplyDamage(battle.Runtime.TryGetAlive(battle.Unit).RequireSome(), 2)));

    battle.Move(battle.Unit, [new Vector3I(2, 0, 1), new Vector3I(3, 0, 1)]);

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

  [TestCase(TestName = "Playback uses the stored unconscious event after the unit changes")]
  public void PlaybackUsesStoredUnconsciousEventAfterUnitChanges()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1));
    var director = battle.OwnNode(new BareDirector()); // unbound: steps are driven directly
    var mesh = battle.OwnNode(new Node3D { Position = BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)) });
    director.RegisterUnitMesh(battle.Unit, mesh);
    var unconscious = new UnitUnconsciousBattleEvent(battle.Unit, battle.At(1, 0, 1), None);

    BattleActionExecResult killResult = battle.ApplyDamage(battle.Unit, 999);
    UnitKilledBattleEvent killed = killResult.EventsThatOccurred.AsValueEnumerable()
      .OfType<UnitKilledBattleEvent>()
      .Single();

    bool done = false;
    director.PlayStepPublic(unconscious, () => done = true);
    Assert.True(done);
    Assert.True(mesh.Visible);

    done = false;
    director.PlayStepPublic(killed, () => done = true);
    Assert.True(done);
    Assert.False(mesh.Visible);
  }

  [TestCase]
  public void StunDamageAndRecoveryPlaybackCompleteInstantly()
  {
    using var battle = BattleFixture.Duel();
    var director = battle.OwnNode(new BareDirector()); // unbound: steps are driven directly
    var mesh = battle.OwnNode(new Node3D());
    director.RegisterUnitMesh(battle.PlayerUnit, mesh);

    battle.ClearEvents();
    battle.ApplyDamage(battle.PlayerUnit, 5, DamageKind.Stun);
    battle.EndFactionTurn(battle.PlayerFaction);
    BattleEvent[] events =
    [
      battle.Events.SingleEvent<UnitDamagedBattleEvent>(),
      battle.Events.SingleEvent<UnitStunRecoveredBattleEvent>(),
    ];

    foreach (BattleEvent battleEvent in events)
    {
      bool done = false;
      director.PlayStepPublic(battleEvent, () => done = true);
      Assert.True(done);
      Assert.True(mesh.Visible);
    }
  }

  [TestCase(TestName = "Move steps for hidden (dead) meshes complete instantly without moving")]
  public void MoveStepsForHiddenMeshesCompleteInstantlyWithoutMoving()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), unitName: "Mover");
    var director = battle.OwnNode(new BareDirector()); // unbound: directly exercises PlayMoveStep
    var mesh = battle.OwnNode(new Node3D { Position = BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)) });
    director.RegisterUnitMesh(battle.Unit, mesh);
    mesh.Visible = false; // killed units hide their mesh; their pending steps must not tween

    var run = new List<UnitMovedBattleEvent>
    {
      new(battle.Unit, battle.At(2, 0, 1), battle.At(1, 0, 1)),
    };
    bool done = false;
    director.MoveStepPublic(run, () => done = true);

    Assert.True(done); // synchronous completion: no tween started
    Assert.Equal(BoardCoordinates.TileToWorldCenter(new Vector3I(1, 0, 1)), mesh.Position);
  }

  // Interrupts with a damage action once the mover reaches the trigger tile.
  private sealed class DamageAfterFirstStepHook(Vector3I triggerPosition, Func<BattleAction> build)
    : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => evt.Position.Raw == triggerPosition ? [build()] : [];
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
      MoveRuns.Add(run.AsValueEnumerable().Select(step => step.Position.Raw).ToList());
      done();
    }
  }

  // No overrides: exposes the production step methods directly.
  private sealed partial class BareDirector : EventPlaybackDirector
  {
    public void MoveStepPublic(IReadOnlyList<UnitMovedBattleEvent> run, Action done) => PlayMoveStep(run, done);
    public void PlayStepPublic(BattleEvent battleEvent, Action done) => PlayStep(battleEvent, done);
  }
}
