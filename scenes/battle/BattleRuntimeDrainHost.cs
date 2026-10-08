using System;
using System.Collections.Generic;
using FunProject.Battle;
using Godot;

// Retains teardown dependencies only. This is not an action or decision scheduler.
public sealed partial class BattleRuntimeDrainHost : Node
{
  private static readonly Dictionary<SceneTree, BattleRuntimeDrainHost> Hosts = [];
  private readonly Dictionary<BattleRuntime, List<Action>> _pending = [];
  private SceneTree? _ownerTree;

  public static void Retain(BattleRuntime runtime, Action afterDrain)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(afterDrain);
    runtime.RequestClose();
    var tree = (SceneTree)Engine.GetMainLoop();
    if (Hosts.TryGetValue(tree, out var existing) && existing._pending.TryGetValue(runtime, out var callbacks))
    {
      callbacks.Add(afterDrain);
      return;
    }
    bool drained;
    try { drained = runtime.TryFinishDisposal(); }
    catch (Exception)
    {
      // Final disposal precedes cancellation fault propagation. Cleanup still belongs
      // to this completed drain, even when no polling host was needed.
      if (runtime.TryFinishDisposal()) afterDrain();
      throw;
    }
    if (drained) { afterDrain(); return; }
    if (!Hosts.TryGetValue(tree, out var host))
    {
      host = new BattleRuntimeDrainHost { Name = "BattleRuntimeDrainHost", ProcessMode = ProcessModeEnum.Always, _ownerTree = tree };
      // Managed retention precedes deferred attachment: AddChild(root) inside _ExitTree is invalid.
      Hosts.Add(tree, host);
      var retained = host;
      Callable.From(() =>
      {
        if (!GodotObject.IsInstanceValid(tree.Root)) return;
        tree.Root.AddChild(retained);
      }).CallDeferred();
    }
    host._pending.Add(runtime, [afterDrain]);
  }

  public override void _Process(double delta) => Tick();
  public void Tick()
  {
    Exception? failure = null;
    foreach (var runtime in _pending.Keys.AsValueEnumerable().ToArray())
    {
      bool drained;
      try { drained = runtime.TryFinishDisposal(); }
      catch (Exception error)
      {
        // Cancellation callback faults surface after final disposal; a second poll is idempotent.
        failure ??= error;
        drained = runtime.TryFinishDisposal();
      }
      if (!drained) continue;
      var callbacks = _pending[runtime];
      _pending.Remove(runtime);
      foreach (var callback in callbacks)
        try { callback(); } catch (Exception error) { failure ??= error; }
    }
    if (_pending.Count == 0)
    {
      if (_ownerTree is not null) Hosts.Remove(_ownerTree);
      QueueFree();
    }
    if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
  }
  public override void _ExitTree()
  {
    foreach (var runtime in _pending.Keys) runtime.RequestClose();
    // Whole-process exit never waits for an admitted worker or cancellation callback.
  }
}
