using Godot;
using System;
using System.Collections.Generic;

// Owns the geoscape view stack: which view is visible, and the pushed views' lifecycle.
// The authored RootView export (the permanent map+HUD view) is installed as the stack
// bottom in _Ready and can never be popped. Push accepts only fresh, unparented, live
// GeoscapeView nodes — caller bugs are rejected before the current view is touched.
// Covered views keep their instance and state but are hidden, processing-disabled, and
// disconnected from navigation; Pop detaches and frees the top immediately. The manager is
// fully generic: it knows no concrete view types, scene factories, session, GameState,
// background rendering, or HUD — data presentation is the composition root's job through
// ViewChanged, which fires once the stack and tree state are already correct. Pushed views
// retain their authored layout.
public sealed partial class GeoscapeViewManager : Control
{
  [Export] public GeoscapeView RootView { get; set; } = null!;

  private readonly Stack<GeoscapeView> _views = new();

  public GeoscapeView Current => _views.Peek();

  [Signal] public delegate void ViewChangedEventHandler(GeoscapeView view);

  public override void _EnterTree()
  {
    // _Ready fires once, but a removed-and-readded manager adopts its retained stack here.
    if (_views.Count > 0)
      SubscribeNavigation(Current);
  }

  public override void _Ready()
  {
    if (RootView is null)
      throw new InvalidOperationException(
        "GeoscapeViewManager requires RootView; assign its direct child in the inspector.");
    if (RootView.GetParent() != this)
      throw new InvalidOperationException(
        "GeoscapeViewManager RootView must be a direct child of the manager.");

    _views.Push(RootView);
    SubscribeNavigation(RootView);
  }

  public void Push(GeoscapeView view)
  {
    if (view is null)
      throw new ArgumentNullException(nameof(view), "Push requires a view.");
    if (!IsInstanceValid(view) || view.IsQueuedForDeletion())
      throw new InvalidOperationException("Push requires a live view not queued for deletion.");
    if (view.GetParent() is not null)
      throw new InvalidOperationException("Push requires an unparented view.");
    // A view already on the stack is parented to the manager, so the check above rejects it.

    // All caller bugs are rejected above, before the active view is touched.
    Deactivate(Current);

    if (view.HidesPreviousScene)
      Current.Hide();

    _views.Push(view);
    AddChild(view);
    SubscribeNavigation(view);
    view.Show();
    view.ProcessMode = ProcessModeEnum.Inherit;
    EmitSignal(SignalName.ViewChanged, view);
  }

  public void Pop()
  {
    if (_views.Count <= 1)
      return; // the permanent root never pops

    GeoscapeView removed = _views.Pop();
    UnsubscribeNavigation(removed);
    RemoveChild(removed); // detach now: popped views must not linger to frame end
    removed.QueueFree();

    GeoscapeView prior = Current;
    SubscribeNavigation(prior);
    prior.Show();
    prior.ProcessMode = ProcessModeEnum.Inherit;
    EmitSignal(SignalName.ViewChanged, prior);
  }

  public override void _ExitTree()
  {
    if (_views.Count > 0)
      UnsubscribeNavigation(Current);
  }

  // Only the stack top may navigate: covered and popped senders stay disconnected.
  private void SubscribeNavigation(GeoscapeView view)
  {
    view.ViewRequested += OnViewRequested;
    view.BackRequested += OnBackRequested;
  }

  private void UnsubscribeNavigation(GeoscapeView view)
  {
    view.ViewRequested -= OnViewRequested;
    view.BackRequested -= OnBackRequested;
  }

  private void OnViewRequested(GeoscapeView view) => Push(view);

  private void OnBackRequested() => Pop();

  private void Deactivate(GeoscapeView view)
  {
    UnsubscribeNavigation(view);
    view.ProcessMode = ProcessModeEnum.Disabled;
  }
}
