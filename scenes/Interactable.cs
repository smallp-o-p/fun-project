using System;
using System.Linq;
using Godot;

public abstract partial class Interactable : Area3D, IInteractable
{
  [Signal]
  public delegate void InteractedEventHandler(Interactable interactable, Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIndex);

  [Export] public bool IsEnabled { get; set; } = true;
  [Export] public MouseButton ActivationButton { get; set; } = MouseButton.Left;

  public bool IsHovered { get; private set; }

  public override void _Ready()
  {
    InputRayPickable = true;

    if (CollisionLayer == 0)
    {
      GD.PushError($"{nameof(Interactable)} '{Name}' needs at least one collision layer to receive mouse input.");
    }

    if (!HasCollisionShape())
    {
      GD.PushError($"{nameof(Interactable)} '{Name}' needs a CollisionShape3D or CollisionPolygon3D child to receive mouse input.");
    }
  }

  public override void _MouseEnter()
  {
    IsHovered = true;
    OnHoverStarted();
  }

  public override void _MouseExit()
  {
    IsHovered = false;
    OnHoverEnded();
  }

  public override void _InputEvent(Camera3D camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, int shapeIdx)
  {
    TryHandleInput(camera, @event, eventPosition, normal, shapeIdx);
  }

  public bool TryHandleInput(Camera3D camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, int shapeIdx)
  {
    ArgumentNullException.ThrowIfNull(camera);
    ArgumentNullException.ThrowIfNull(@event);

    if (!IsEnabled)
    {
      return false;
    }

    if (@event is not InputEventMouseButton mouseButtonEvent)
    {
      return false;
    }

    if (!mouseButtonEvent.Pressed || mouseButtonEvent.ButtonIndex != ActivationButton)
    {
      return false;
    }

    if (!CanInteract(camera, eventPosition, normal, shapeIdx))
    {
      return false;
    }

    HandleInteraction(camera, eventPosition, normal, shapeIdx);
    return true;
  }

  public abstract bool CanInteract(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx);

  public abstract void OnInteracted(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx);

  public abstract void OnHoverStarted();

  public abstract void OnHoverEnded();

  private void HandleInteraction(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx)
  {
    ArgumentNullException.ThrowIfNull(camera);

    OnInteracted(camera, eventPosition, normal, shapeIdx);
    EmitSignal(SignalName.Interacted, this, camera, eventPosition, normal, shapeIdx);
  }

  private bool HasCollisionShape()
  {
    return GetChildren().Any(node => node is CollisionShape3D || node is CollisionPolygon3D);
  }
}
