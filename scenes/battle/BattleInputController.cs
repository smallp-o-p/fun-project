using FunProject.Battle;
using Godot;
using System;

// Thin Godot shell: translates pointer/keyboard input into BattleUiController intents and
// reports hover-preview repaint requests to the scene.
public sealed partial class BattleInputController : Node
{
  private BattleUiController _ui = null!;
  private Camera3D _camera = null!;

  public event Action PreviewUpdated = delegate { };

  public void Initialize(BattleUiController ui, Camera3D camera)
  {
    ArgumentNullException.ThrowIfNull(ui);
    ArgumentNullException.ThrowIfNull(camera);
    _ui = ui;
    _camera = camera;
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (@event is InputEventMouseMotion)
    {
      UpdateHoverPreview();
      return;
    }

    if (@event is InputEventMouseButton mouse && mouse.Pressed)
    {
      if (mouse.ButtonIndex == MouseButton.Left)
        HandleLeftClick();
      else if (mouse.ButtonIndex == MouseButton.Right)
        _ui.Cancel();
      return;
    }

    if (@event.IsActionPressed("ui_accept"))
      _ui.Confirm();
    else if (@event.IsActionPressed("ui_cancel"))
      _ui.Cancel();
  }

  private void HandleLeftClick()
  {
    if (TryPickTile(out Vector3I tile))
      _ui.ClickTile(tile);
  }

  private void UpdateHoverPreview()
  {
    if (_ui.State != UiState.Targeting)
      return;
    if (!TryPickTile(out Vector3I tile))
      return;

    _ui.PreviewAt(tile);
    PreviewUpdated.Invoke();
  }

  private bool TryPickTile(out Vector3I tile)
  {
    // Picks the board tile under the cursor by raycasting the flat ground collider. Units have no
    // collider, so clicking "on" a unit still resolves to its tile; selection/targeting recover the unit.
    tile = Vector3I.Zero;
    Vector2 mousePosition = _camera.GetViewport().GetMousePosition();
    World3D world = _camera.GetWorld3D();
    Option<Vector3> hit = GameCamera.TryRaycastViewportPosition(_camera, world, mousePosition);
    if (hit.IsNone)
      return false;
    tile = BoardCoordinates.WorldToTile(hit.Match(v => v, () => Vector3.Zero));
    return true;
  }
}
