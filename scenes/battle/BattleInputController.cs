using FunProject.Battle;
using Godot;
using System;

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
    if (@event is InputEventMouseMotion)
    {
      UpdateHover();
      return;
    }

    if (@event is InputEventMouseButton mouse && mouse.Pressed)
    {
      if (mouse.ButtonIndex == MouseButton.Left)
        HandleLeftClick();
      else if (mouse.ButtonIndex == MouseButton.Right)
        CancelPressed?.Invoke();
      return;
    }

    if (@event.IsActionPressed("ui_accept"))
      ConfirmPressed?.Invoke();
    else if (@event.IsActionPressed("ui_cancel"))
      CancelPressed?.Invoke();
  }

  private void HandleLeftClick()
  {
    if (TryPickTile(out Vector3I tile))
      TileClicked?.Invoke(tile);
  }

  private void UpdateHover()
  {
    if (TryPickTile(out Vector3I tile))
      TileHovered?.Invoke(tile);
  }

  private bool TryPickTile(out Vector3I tile)
  {
    // Picks the board tile under the cursor by raycasting the map's ground collider. Units have no
    // collider, so clicking "on" a unit still resolves to its tile; selection/targeting recover the unit.
    tile = Vector3I.Zero;
    Vector2 mousePosition = _cameraRig.GetViewport().GetMousePosition();
    Option<Vector3> hit = _cameraRig.TryRaycastViewportPosition(mousePosition);
    if (hit.IsNone)
      return false;
    tile = BoardCoordinates.WorldToTile(hit.Match(v => v, () => Vector3.Zero));
    return true;
  }
}
