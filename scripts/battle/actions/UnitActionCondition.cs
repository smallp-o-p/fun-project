using System.Linq;

namespace FunProject.Battle;

// Availability conditions for unit action verbs. Identity is the concrete subclass (no enums):
// callers receive a failed condition as the object itself and dispatch on its type. IsMet is
// internal and query-side only (GetAvailableActionsForUnit), so it takes a provably-alive
// AliveUnit; presentation sees identity, never evaluates.
public abstract class UnitActionCondition
{
  internal abstract bool IsMet(BattleSession session, AliveUnit unit);
}

// Battle is in progress and the scheduler allows this unit to act right now
// (active side, still available this turn, alive, not immobilized).
public sealed class UnitCanActNowCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => session.Phase == BattlePhase.InProgress && session.CanUnitActNow(unit.State);
}

public sealed class HasActionPointsCondition : UnitActionCondition
{
  public int Cost { get; }

  public HasActionPointsCondition(int cost)
  {
    Cost = cost;
  }

  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.CurrentActionPoints >= Cost;
}

// At least one adjacent tile is occupiable — the board's cheap no-BFS proxy for "some move
// exists", sharing the pathfinder's own neighborhood so the two can never drift.
public sealed class HasOpenAdjacentTileCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => session.Board.HasOccupiableNeighbor(unit.Position);
}

// Weapons without a magazine are always loaded.
public sealed class WeaponIsLoadedCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.EquippedWeapon.Match(
      Some: weapon => weapon.IsLoaded,
      None: () => false);
}

// Some visible unit is a legal attack target right now. Per-target legality delegates to
// AttackFeasibility, so this can never drift from what AttackUnit accepts at submit time.
public sealed class HasAttackableTargetCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.VisibleUnits.Any(target => AttackFeasibility.Resolve(session, unit.State, target).IsRight);
}

public sealed class CanReloadCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.EquippedWeapon.Match(
      Some: weapon => weapon.CanReload(),
      None: () => false);
}

public sealed class IsActiveSideCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => session.Phase == BattlePhase.InProgress && unit.State.Side == session.ActiveSide;
}
