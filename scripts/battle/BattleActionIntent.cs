#nullable enable
using Godot;
using System;

namespace FunProject.Battle;

public delegate bool BattleActionResolver(BattleSession session, BattleActionIntent intent);

public sealed class BattleActionIntent
{
  public string ActionId { get; }
  public int UnitId { get; }
  public Vector3I? TargetCell { get; }
  public int? TargetUnitId { get; }
  public Variant? Payload { get; }

  private readonly BattleActionResolver _resolver;

  public BattleActionIntent(
    string actionId,
    int unitId,
    BattleActionResolver resolver,
    Vector3I? targetCell = null,
    int? targetUnitId = null,
    Variant? payload = null)
  {
    ActionId = actionId;
    UnitId = unitId;
    TargetCell = targetCell;
    TargetUnitId = targetUnitId;
    Payload = payload;
    _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
  }

  public bool Apply(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return _resolver(session, this);
  }

  public BattleActionIntent WithResolver(BattleActionResolver resolver)
  {
    return new BattleActionIntent(ActionId, UnitId, resolver, TargetCell, TargetUnitId, Payload);
  }

  public static BattleActionIntent MoveStep(int unitId, Vector3I targetCell, int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return new BattleActionIntent(
      actionId: "move_step",
      unitId: unitId,
      resolver: (session, intent) =>
      {
        if (!intent.TargetCell.HasValue)
          return false;

        return session.TryMoveUnitStep(intent.UnitId, intent.TargetCell.Value, actionPointCost);
      },
      targetCell: targetCell,
      payload: Variant.From(actionPointCost));
  }

  public static BattleActionIntent Move(int unitId, Vector3I targetCell, int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return MoveStep(unitId, targetCell, actionPointCost);
  }

  public static BattleActionIntent EndTurn(int unitId)
  {
    return new BattleActionIntent(
      actionId: "end_turn",
      unitId: unitId,
      resolver: (session, _) =>
      {
        session.AdvanceTurn();
        return true;
      });
  }

  public static BattleActionIntent Custom(
    string actionId,
    int unitId,
    BattleActionResolver resolver,
    Vector3I? targetCell = null,
    int? targetUnitId = null,
    Variant? payload = null)
  {
    return new BattleActionIntent(actionId, unitId, resolver, targetCell, targetUnitId, payload);
  }
}
