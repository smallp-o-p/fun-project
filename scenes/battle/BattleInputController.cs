using FunProject.Battle;
using Godot;
using System;

namespace FunProject.Scenes;

// Thin Godot shell: translates pointer/keyboard input into unit, tile and intent events by raycasting
// through the camera rig; no UI knowledge.
public sealed partial class BattleInputController : Node
{
  private GameCamera _cameraRig = null!;
  private BattleUnitMeshes _unitMeshes = null!;

  public event Action<BattleUnitState>? UnitClicked;
  public event Action<BattleUnitState>? UnitHovered;
  public event Action<Vector3I>? TileClicked;
  public event Action<Vector3I>? TileHovered;
  public event Action? HoverCleared;
  public event Action? ConfirmPressed;
  public event Action? CancelPressed;

  public void Initialize(GameCamera cameraRig, BattleUnitMeshes unitMeshes)
  {
    ArgumentNullException.ThrowIfNull(cameraRig);
    ArgumentNullException.ThrowIfNull(unitMeshes);
    _cameraRig = cameraRig;
    _unitMeshes = unitMeshes;
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (@event is InputEventMouseMotion motion)
    {
      Pick(motion.Position, clicked: false);
      return;
    }

    if (@event is InputEventMouseButton mouse && mouse.Pressed)
    {
      if (mouse.ButtonIndex == MouseButton.Left)
        Pick(mouse.Position, clicked: true);
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
  private void Pick(Vector2 position, bool clicked)
  {
    // One nearest hit across terrain and unit areas preserves occlusion. Built-in object
    // picking runs after _UnhandledInput, so mixing it with tile input would act twice.
    var hit = _cameraRig.RaycastViewportPosition(position,
      collisionMask: 1 | 2 | BattleUnitMeshes.PickingLayer, collideWithAreas: true);
    if (hit.Count == 0)
    {
      if (!clicked) HoverCleared?.Invoke();
      return;
    }
    var collider = hit["collider"].AsGodotObject();
    uint layer = collider switch
    {
      CollisionObject3D body => body.CollisionLayer,
      GridMap grid => grid.CollisionLayer,
      _ => 0,
    };
    if (_unitMeshes.GetUnitForHitbox(collider).Case is BattleUnitState unit)
    {
      if (clicked) UnitClicked?.Invoke(unit);
      else UnitHovered?.Invoke(unit);
    }
    else if ((layer & 2) == 0)
    {
      Vector3I tile = BoardCoordinates.WorldToTile(hit["position"].AsVector3());
      if (clicked) TileClicked?.Invoke(tile);
      else TileHovered?.Invoke(tile);
    }
    else if (!clicked)
      HoverCleared?.Invoke();
    if (clicked) GetViewport().SetInputAsHandled();
  }
}
