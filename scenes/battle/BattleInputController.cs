using FunProject.Battle;
using Godot;
using System;

namespace FunProject.Scenes;

// Thin Godot shell: translates pointer/keyboard input into tile and intent events by raycasting
// through the camera rig; no UI knowledge.
public sealed partial class BattleInputController : Node
{
  private GameCamera _cameraRig = null!;

  public event Action<Vector3I>? TileClicked;
  public event Action<Vector3I>? TileHovered;
  public event Action? ConfirmPressed;
  public event Action? CancelPressed;

  public void Initialize(GameCamera cameraRig)
  {
    ArgumentNullException.ThrowIfNull(cameraRig);
    _cameraRig = cameraRig;
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (@event is InputEventMouseMotion motion)
    {
      PickTile(motion.Position).IfSome(tile => TileHovered?.Invoke(tile));
      return;
    }

    if (@event is InputEventMouseButton mouse && mouse.Pressed)
    {
      if (mouse.ButtonIndex == MouseButton.Left)
        PickTile(mouse.Position).IfSome(tile => TileClicked?.Invoke(tile));
      else if (mouse.ButtonIndex == MouseButton.Right)
        CancelPressed?.Invoke();
      return;
    }

    if (@event.IsActionPressed("ui_accept"))
      ConfirmPressed?.Invoke();
    else if (@event.IsActionPressed("ui_cancel"))
      CancelPressed?.Invoke();
  }

  // Delivered pointer coordinates belong to this viewport; cursor polling can see a later event.
  private Option<Vector3I> PickTile(Vector2 position) =>
    _cameraRig.TryRaycastViewportPosition(position).Map(BoardCoordinates.WorldToTile);
}
