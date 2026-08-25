using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Battle;

public abstract class BattleAction
{
  public enum Result
  {
    Completed,
    Interrupted,
    Rejected,
    Incomplete,
  }

  /// <summary>
  /// Execute a single step in the action. Returns the result, which indicates to the executor whether the action is finished.
  /// </summary>
  /// <param name="session"></param>
  public abstract Result Execute(BattleSession session);

  public static StartBattle StartBattle()
  {
    return new StartBattle();
  }

  public static SpawnUnit SpawnUnit(Combatant combatant, BattleBoardState.ValidatedPoint position)
  {
    return new SpawnUnit(combatant, position);
  }

  public static SpawnUnit SpawnUnit(Combatant combatant, BattleBoardState.ValidatedPoint position, Weapon equippedWeapon)
  {
    return new SpawnUnit(combatant, position, equippedWeapon);
  }

  public static PlaceObject PlaceObject(BattleSpecialObjectData data, BattleBoardState.ValidatedPoint position)
  {
    return new PlaceObject(data, position);
  }

  public static MoveUnit MoveUnit(
    AliveUnit unit,
    IEnumerable<BattleBoardState.ValidatedPoint> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost
  )
  {
    return new MoveUnit(unit, destinations, actionPointCostPerStep);
  }

  public static InteractWithObject InteractWithObject(AliveUnit unit, LiveObject obj)
  {
    return new InteractWithObject(unit, obj);
  }

  public static AttackUnit AttackUnit(AliveUnit attacker, AliveUnit target)
  {
    return new AttackUnit(attacker, target);
  }

  public static ThrowItem ThrowItem(AliveUnit unit, ItemWith<ThrowableCapability> throwable, BattleBoardState.ValidatedPoint targetCell)
  {
    return new ThrowItem(unit, throwable, targetCell);
  }

  public static UseItem UseItem(AliveUnit unit, ItemWith<ChargesCapability> usable)
  {
    return new UseItem(unit, usable);
  }

  public static ReloadWeapon ReloadWeapon(AliveUnit unit, AmmunitionedWeapon weapon)
  {
    return new ReloadWeapon(unit, weapon);
  }

  public static ApplyDamage ApplyDamage(AliveUnit unit, int amount)
  {
    return new ApplyDamage(unit, amount);
  }

  public static PassUnit PassUnit(AliveUnit unit)
  {
    return new PassUnit(unit);
  }

  public static EndFactionTurn EndFactionTurn(Faction expectedActiveSide)
  {
    return new EndFactionTurn(expectedActiveSide);
  }
}

public sealed class MoveUnit : BattleAction
{
  private readonly IReadOnlyList<BattleBoardState.ValidatedPoint> _requestedDestinations;
  private Queue<BattleBoardState.ValidatedPoint>? _validatedRoute;

  private BattleUnitState Unit { get; }
  private int StepApCost { get; }

  internal MoveUnit(
    AliveUnit unit,
    IEnumerable<BattleBoardState.ValidatedPoint> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
  {
    ArgumentNullException.ThrowIfNull(destinations);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);

    Unit = unit.State;
    _requestedDestinations = [.. destinations];
    StepApCost = actionPointCostPerStep;
  }

  public override Result Execute(BattleSession session)
  {
    var route = _validatedRoute ?? ValidateRoute(session);

    if (route is null)
      return Result.Rejected;

    if (route.Count == 0)
      return Result.Completed;

    return session.TryGetAlive(Unit)
      .Match(
        unit =>
        {
          if (!session.Board.CanOccupy(route.Peek()))
            return Result.Rejected;

          Unit.SpendActionPoints(StepApCost);
          session.MoveUnit(unit.State, unit.Position, route.Peek());

          route.Dequeue();
          return route.Count != 0 ? Result.Incomplete : Result.Completed;
        },
        () => Result.Interrupted
      );
  }

  private Queue<BattleBoardState.ValidatedPoint>? ValidateRoute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (_requestedDestinations.Count == 0)
      return null;

    long apCost = (long)_requestedDestinations.Count * StepApCost;

    if (Unit.CurrentActionPoints < apCost)
      return null;

    BattleBoardState.ValidatedPoint previousPoint = session.GetUnitPosition(Unit).ValueUnsafe();
    Queue<BattleBoardState.ValidatedPoint> validatedSteps = [];

    // Steps arrive as ValidatedPoints (in-bounds is proven at the caller's mint door); only
    // the mutable facts — adjacency to the evolving position and occupancy — are checked here.
    foreach (BattleBoardState.ValidatedPoint stepPoint in _requestedDestinations)
    {
      if (!BattleBoardState.AreAdjacent(previousPoint, stepPoint) || !session.Board.CanOccupy(stepPoint))
        return null;

      validatedSteps.Enqueue(stepPoint);
      previousPoint = stepPoint;
    }

    _validatedRoute = validatedSteps;
    return _validatedRoute;
  }
}
