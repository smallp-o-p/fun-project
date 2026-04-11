#nullable enable
using FunProject.Items;
using Godot;
using System;

namespace FunProject.Battle;

public delegate bool BattleActionResolver(BattleSession session, CustomBattleActionIntent intent);

public abstract class BattleActionIntent
{
  public const string MoveStepActionId = "move_step";
  public const string PassUnitActionId = "pass_unit";
  public const string EndFactionTurnActionId = "end_faction_turn";
  public const string ThrowItemActionId = "throw_item";

  public string ActionId { get; }

  protected BattleActionIntent(string actionId)
  {
    if (string.IsNullOrWhiteSpace(actionId))
      throw new ArgumentException("Action id cannot be null or whitespace.", nameof(actionId));

    ActionId = actionId;
  }

  public static MoveStepBattleActionIntent MoveStep(int unitId, Vector3I targetCell, int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return new MoveStepBattleActionIntent(unitId, targetCell, actionPointCost);
  }

  public static MoveStepBattleActionIntent Move(int unitId, Vector3I targetCell, int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return MoveStep(unitId, targetCell, actionPointCost);
  }

  public static PassUnitBattleActionIntent PassUnit(int unitId)
  {
    return new PassUnitBattleActionIntent(unitId);
  }

  public static EndFactionTurnBattleActionIntent EndFactionTurn(Faction issuingSide)
  {
    return new EndFactionTurnBattleActionIntent(issuingSide);
  }

  public static ThrowItemBattleActionIntent ThrowItem(int unitId, ThrowableItem item, Vector3I targetCell)
  {
    return new ThrowItemBattleActionIntent(unitId, item, targetCell);
  }

  public static CustomBattleActionIntent Custom(
    string actionId,
    BattleActionResolver resolver,
    int? unitId = null,
    Variant? payload = null)
  {
    return new CustomBattleActionIntent(actionId, resolver, unitId, payload);
  }

  public static NamedBattleActionIntent Named(string actionId, int? unitId = null)
  {
    return new NamedBattleActionIntent(actionId, unitId);
  }
}

public abstract class UnitBattleActionIntent : BattleActionIntent
{
  public int UnitId { get; }

  protected UnitBattleActionIntent(string actionId, int unitId) : base(actionId)
  {
    UnitId = unitId;
  }
}

public sealed class MoveStepBattleActionIntent : UnitBattleActionIntent
{
  public Vector3I TargetCell { get; }
  public int ActionPointCost { get; }

  public MoveStepBattleActionIntent(int unitId, Vector3I targetCell, int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
    : base(MoveStepActionId, unitId)
  {
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    TargetCell = targetCell;
    ActionPointCost = actionPointCost;
  }
}

public sealed class PassUnitBattleActionIntent : UnitBattleActionIntent
{
  public PassUnitBattleActionIntent(int unitId)
    : base(PassUnitActionId, unitId)
  {
  }
}

public sealed class EndFactionTurnBattleActionIntent : BattleActionIntent
{
  public Faction IssuingSide { get; }

  public EndFactionTurnBattleActionIntent(Faction issuingSide)
    : base(EndFactionTurnActionId)
  {
    IssuingSide = issuingSide ?? throw new ArgumentNullException(nameof(issuingSide));
  }
}

public sealed class ThrowItemBattleActionIntent : UnitBattleActionIntent
{
  public ThrowableItem Item { get; }
  public Vector3I TargetCell { get; }

  public ThrowItemBattleActionIntent(int unitId, ThrowableItem item, Vector3I targetCell)
    : base(ThrowItemActionId, unitId)
  {
    Item = item ?? throw new ArgumentNullException(nameof(item));
    TargetCell = targetCell;
  }
}

public sealed class CustomBattleActionIntent : BattleActionIntent
{
  public int? UnitId { get; }
  public Variant? Payload { get; }

  private readonly BattleActionResolver _resolver;

  public CustomBattleActionIntent(
    string actionId,
    BattleActionResolver resolver,
    int? unitId = null,
    Variant? payload = null)
    : base(actionId)
  {
    _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    UnitId = unitId;
    Payload = payload;
  }

  internal bool Resolve(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return _resolver(session, this);
  }
}

public sealed class NamedBattleActionIntent : BattleActionIntent
{
  public int? UnitId { get; }

  public NamedBattleActionIntent(string actionId, int? unitId = null)
    : base(actionId)
  {
    UnitId = unitId;
  }
}
