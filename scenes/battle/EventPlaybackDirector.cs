using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

// Visual playback of committed battle events. Subscribes to the runtime's ActionCompleted
// signal, flattens each result's EventsThatOccurred into one FIFO queue, and drains it one
// step per Tick (called from _Process). Consecutive per-tile move events for one unit are
// COALESCED into a single path step at dequeue time, so movement animates as one continuous
// path; anything that interleaves (an interrupting shot, another unit's move) breaks the run
// and plays between the path segments, preserving commit order. Deliberately dumb otherwise:
// no parallel tracks, no seeking, no cancellation. v1 visuals: paths tween the unit mesh at
// constant speed; deaths hide it; everything else is instant.
public partial class EventPlaybackDirector : Node
{
  private const float WorldUnitsPerSecond = 4f; // one orthogonal tile per 0.25s

  private readonly Queue<BattleEvent> _queue = new();
  private readonly Dictionary<BattleUnitState, Node3D> _unitMeshes = [];
  private bool _stepInProgress;
  private bool _wasBusy;

  public bool Busy => _stepInProgress || _queue.Count > 0;

  public event Action PlaybackIdle = delegate { };

  public void Bind(BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    runtime.ActionCompleted += EnqueueResult;
  }

  public void RegisterUnitMesh(BattleUnitState unit, Node3D mesh)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(mesh);
    _unitMeshes[unit] = mesh;
  }

  public override void _Process(double delta) => Tick();

  // Drains at most one step per call: a coalesced move path, or a single event. Fires
  // PlaybackIdle once when the queue empties.
  public void Tick()
  {
    if (_stepInProgress)
      return;

    if (_queue.Count > 0)
    {
      _wasBusy = true;
      if (_queue.Peek() is UnitMovedBattleEvent)
      {
        List<UnitMovedBattleEvent> run = ExtractMoveRun();
        _stepInProgress = true;
        PlayMoveStep(run, OnStepDone);
      }
      else
      {
        BattleEvent next = _queue.Dequeue();
        _stepInProgress = true;
        PlayStep(next, OnStepDone);
      }

      RaiseIdleIfDrained();
      return;
    }

    RaiseIdleIfDrained();
  }

  private void RaiseIdleIfDrained()
  {
    if (_wasBusy && !Busy)
    {
      _wasBusy = false;
      PlaybackIdle.Invoke();
    }
  }

  private void EnqueueResult(BattleActionExecResult result)
  {
    foreach (BattleEvent battleEvent in result.EventsThatOccurred)
      _queue.Enqueue(battleEvent);
  }

  // Pulls the maximal run of consecutive move steps for ONE unit out of the queue. The
  // per-step TileOccupied bookkeeping that follows each move of the same run has no visual
  // of its own, so it folds into the run instead of breaking the path; anything else
  // (a foreign unit's move, an interrupt's events) breaks the run and stays queued.
  private List<UnitMovedBattleEvent> ExtractMoveRun()
  {
    var run = new List<UnitMovedBattleEvent>();
    BattleUnitState unit = _queue.Peek() is UnitMovedBattleEvent first ? first.Unit : null!;

    while (_queue.Count > 0)
    {
      BattleEvent head = _queue.Peek();
      if (head is UnitMovedBattleEvent moved && moved.Unit == unit)
      {
        run.Add(moved);
        _queue.Dequeue();
      }
      else if (run.Count > 0
        && head is TileOccupiedBattleEvent occupied
        && occupied.Unit == unit
        && occupied.Position == run[^1].Position)
      {
        _queue.Dequeue(); // same-run bookkeeping: no visual, fold it in
      }
      else
      {
        break;
      }
    }

    return run;
  }

  private void OnStepDone() => _stepInProgress = false;

  // One visual step per non-move event. Subclasses (tests) override to control completion
  // timing. Move events never reach this method - they are coalesced into PlayMoveStep.
  protected virtual void PlayStep(BattleEvent battleEvent, Action done)
  {
    switch (battleEvent)
    {
      case UnitKilledBattleEvent killed when _unitMeshes.TryGetValue(killed.Unit, out Node3D? deadMesh) && deadMesh is not null:
        deadMesh.Visible = false;
        done();
        break;
      default:
        done(); // no v1 visual: instant
        break;
    }
  }

  // One continuous path step for a coalesced run of move events: constant speed along the
  // waypoints. A hidden mesh (killed mid-action) completes instantly without tweening.
  protected virtual void PlayMoveStep(IReadOnlyList<UnitMovedBattleEvent> run, Action done)
  {
    if (!_unitMeshes.TryGetValue(run[0].Unit, out Node3D? mesh) || mesh is null || !mesh.Visible)
    {
      done();
      return;
    }

    Tween tween = CreateTween();
    foreach (UnitMovedBattleEvent step in run)
    {
      Vector3 destination = BoardCoordinates.TileToWorldCenter(step.Position.Raw) + new Vector3(0f, 0.5f, 0f);
      float segmentDuration = mesh.Position.DistanceTo(destination) / WorldUnitsPerSecond;
      tween.TweenProperty(mesh, "position", destination, segmentDuration);
    }

    tween.TweenCallback(Callable.From(done));
  }
}
