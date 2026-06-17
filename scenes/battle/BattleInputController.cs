using FunProject.Battle;
using Godot;
using System.Collections.Generic;
using System.Linq;

// Thin Godot shell: rebuilds a verb button per available action, maps input to PlayerActionController,
// and renders the preview per targeting kind (path line vs target highlight + hit-% label).
public sealed partial class BattleInputController : Node
{
  private PlayerActionController _controller = null!;
  private Camera3D _camera = null!;
  private MovementLine _movementLine = null!;
  private ReachableTileHighlighter _highlighter = null!;
  private Container _verbButtons = null!;
  private Label _hitChanceLabel = null!;

  public void Initialize(
    PlayerActionController controller,
    Camera3D camera,
    MovementLine movementLine,
    ReachableTileHighlighter highlighter,
    Container verbButtons,
    Label hitChanceLabel)
  {
    _controller = controller;
    _camera = camera;
    _movementLine = movementLine;
    _highlighter = highlighter;
    _verbButtons = verbButtons;
    _hitChanceLabel = hitChanceLabel;
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
        OnCancelPressed();
      return;
    }

    if (@event.IsActionPressed("ui_accept"))
      OnConfirmPressed();
    else if (@event.IsActionPressed("ui_cancel"))
      OnCancelPressed();
  }

  public void OnConfirmPressed()
  {
    if (_controller.Mode == PlayerActionController.TargetingMode.ActionPending)
    {
      _controller.Confirm();
      RefreshAll();
    }
  }

  public void OnCancelPressed()
  {
    _controller.Cancel();
    RefreshAll();
  }

  private void HandleLeftClick()
  {
    if (!TryPickTile(out Vector3I tile))
      return;

    switch (_controller.Mode)
    {
      case PlayerActionController.TargetingMode.None:
        _controller.TrySelectUnitAt(tile);
        break;
      case PlayerActionController.TargetingMode.ActionTargeting:
      case PlayerActionController.TargetingMode.ActionPending:
        _controller.SetPending(tile);
        break;
    }

    RefreshAll();
  }

  private void UpdateHoverPreview()
  {
    if (_controller.Mode != PlayerActionController.TargetingMode.ActionTargeting)
      return;
    if (!TryPickTile(out Vector3I tile))
    {
      HidePreview();
      return;
    }

    _controller.PreviewAt(tile).Match(
      Right: RenderPreview,
      Left: _ => HidePreview());
  }

  // Rebuilds the verb buttons (one per available option) + the candidate highlights + any locked preview.
  private void RefreshAll()
  {
    RebuildVerbButtons();

    PlayerActionController.TargetingMode mode = _controller.Mode;
    bool targeting = mode is PlayerActionController.TargetingMode.ActionTargeting
      or PlayerActionController.TargetingMode.ActionPending;
    if (targeting)
      _highlighter.Show(_controller.CandidateCells);
    else
      _highlighter.Clear();

    if (mode == PlayerActionController.TargetingMode.ActionPending)
      _controller.LastPreview.IfSome(RenderPreview);
    else if (mode != PlayerActionController.TargetingMode.ActionTargeting)
      HidePreview();
  }

  private void RebuildVerbButtons()
  {
    foreach (Node child in _verbButtons.GetChildren())
      child.QueueFree();

    // This increment renders only the available verbs (disabled-vs-omitted is a future increment).
    foreach (UnitActionOption option in _controller.ActionOptions.Where(o => o.IsAvailable))
    {
      var button = new Button { Text = LabelFor(option) };
      UnitActionOption captured = option;
      button.Pressed += () => { _controller.BeginAction(captured); RefreshAll(); };
      _verbButtons.AddChild(button);
    }
  }

  private static string LabelFor(UnitActionOption option) => option switch
  {
    MoveActionOption => "Move",
    AttackActionOption => "Attack",
    PassActionOption => "Pass",
    EndTurnActionOption => "End Turn",
    _ => option.GetType().Name,
  };

  private void RenderPreview(ActionPreview preview)
  {
    switch (preview.Kind)
    {
      case TargetingKind.TilePath:
        _hitChanceLabel.Visible = false;
        DrawLine(preview.Path);
        break;
      case TargetingKind.EnemyTarget:
        _movementLine.Visible = false;
        _hitChanceLabel.Text = preview.HitChance is { } hc ? $"{hc.FinalChance}%" : "";
        _hitChanceLabel.Visible = preview.HitChance is not null;
        break;
    }
  }

  private void HidePreview()
  {
    _movementLine.Visible = false;
    _hitChanceLabel.Visible = false;
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

  private void DrawLine(IReadOnlyList<Vector3I> path)
  {
    if (path.Count < 2)
    {
      _movementLine.Visible = false;
      return;
    }

    _movementLine.Points = path
      .Select(tile => BoardCoordinates.TileToWorldCenter(tile) + new Vector3(0f, 0.05f, 0f))
      .ToArray();
    _movementLine.Rebuild();
    _movementLine.Visible = true;
  }
}
