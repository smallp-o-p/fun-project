using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages views in the Geoscape via a stack. The current view is on top of the stack.
/// </summary>
public sealed partial class GeoscapeViewManager : Control
{
  [Export] public GeoscapeView RootView { get; set; } = null!;

  private readonly Stack<GeoscapeView> _views = new();

  public GeoscapeView Current => _views.Peek();

  [Signal] public delegate void ViewChangedEventHandler(GeoscapeView view);

  public override void _Ready()
  {
    ArgumentNullException.ThrowIfNull(RootView);

    _views.Push(RootView);
    SubscribeNavigation(RootView);
  }

  public override void _ExitTree()
  {
    if(_views.Count > 0)
      UnsubscribeNavigation(Current);
  }

  public void Push(GeoscapeView view)
  {
    if (!IsInstanceValid(view) || view.IsQueuedForDeletion())
      throw new InvalidOperationException("Push requires a live view not queued for deletion.");
    if (view.GetParent() is not null)
      throw new InvalidOperationException("Push requires an unparented view.");

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
      return;

    GeoscapeView removed = _views.Pop();
    UnsubscribeNavigation(removed);
    RemoveChild(removed);
    removed.QueueFree();

    SubscribeNavigation(Current);
    Current.Show();
    Current.ProcessMode = ProcessModeEnum.Inherit;
    EmitSignal(SignalName.ViewChanged, Current);
  }
  
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
