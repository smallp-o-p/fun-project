
namespace FunProject.Battle;

/// <summary>
/// A fancy way to check if a unit can do a certain action. Unit actions should compose these condition objects
/// to check its availability.
/// </summary>
public abstract class UnitActionCondition
{
  internal abstract bool IsMet(BattleSession session, AliveUnit unit);
}

// Battle is in progress and the scheduler allows this unit to act right now
// (active side, still available this turn, alive, and not incapacitated).
public sealed class UnitCanActNowCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => session.Phase == BattlePhase.InProgress && session.CanUnitActNow(unit.State);
}

public sealed class HasActionPointsCondition(int cost) : UnitActionCondition
{
  public int Cost { get; } = cost;

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
// AttackContext.Resolve, so this can never drift from what AttackUnit accepts at submit time.
public sealed class HasAttackableTargetCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.VisibleUnits.AsValueEnumerable().Any(target => AttackContext.Resolve(session, unit.State, target).IsRight);
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
