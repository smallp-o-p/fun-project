
namespace FunProject.Battle;

/// <summary>
/// A fancy way to check if a unit can do a certain action. Unit actions should compose these condition objects
/// to check its availability.
/// </summary>
public abstract class UnitActionCondition
{
  internal abstract bool IsMet(BattleSession session, AliveUnit unit);
  internal abstract bool MayChange(BattleEvent battleEvent, BattleUnitState unit);

  internal static bool IncapacityMayChange(BattleEvent battleEvent, BattleUnitState unit) => battleEvent switch
  {
    UnitDamagedBattleEvent damaged => damaged.Unit == unit,
    UnitKilledBattleEvent killed => killed.Unit == unit,
    UnitUnconsciousBattleEvent unconscious => unconscious.Unit == unit,
    UnitStunRecoveredBattleEvent recovered => recovered.Unit == unit,
    UnitStatusEffectAppliedBattleEvent applied => applied.Unit == unit,
    UnitStatusEffectTickedBattleEvent ticked => ticked.Unit == unit,
    UnitStatusEffectExpiredBattleEvent expired => expired.Unit == unit,
    UnitBuffActivatedBattleEvent activated => activated.Unit == unit,
    UnitBuffDeactivatedBattleEvent deactivated => deactivated.Unit == unit,
    _ => false,
  };

  protected static bool ActionPointsMayChange(BattleEvent battleEvent, BattleUnitState unit) => battleEvent switch
  {
    UnitMovedBattleEvent moved => moved.Unit == unit,
    UnitAttackedBattleEvent attacked => attacked.Unit == unit,
    UnitReloadedWeaponBattleEvent reloaded => reloaded.Unit == unit,
    ItemUsedBattleEvent used => used.Unit == unit,
    ItemThrownBattleEvent thrown => thrown.Unit == unit,
    ObjectInteractedBattleEvent interacted => interacted.Actor == unit,
    TurnStartedBattleEvent => true,
    UnitBuffActivatedBattleEvent activated => activated.Unit == unit,
    UnitBuffDeactivatedBattleEvent deactivated => deactivated.Unit == unit,
    _ => false,
  };

  protected static bool WeaponMayChange(BattleEvent battleEvent, BattleUnitState unit)
    => unit.EquippedWeapon.Match(
      Some: weapon => battleEvent switch
      {
        UnitAttackedBattleEvent attacked => attacked.Weapon == weapon,
        UnitReloadedWeaponBattleEvent reloaded => reloaded.Weapon == weapon,
        _ => false,
      },
      None: () => false);

  protected static bool PhaseOrSideMayChange(BattleEvent battleEvent) => battleEvent is
    SessionStartedBattleEvent or
    SessionEndedBattleEvent or
    TurnStartedBattleEvent or
    TurnEndedBattleEvent or
    ActiveSideChangedBattleEvent;

  protected static bool SchedulingMayChange(BattleEvent battleEvent, BattleUnitState unit)
    => PhaseOrSideMayChange(battleEvent)
      || battleEvent is UnitActivationEndedBattleEvent ended && ended.Unit == unit;
}

// Battle is in progress and the scheduler allows this unit to act right now
// (active side, still available this turn, alive, and not incapacitated).
public sealed class UnitCanActNowCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => session.Phase == BattlePhase.InProgress && session.CanUnitActNow(unit.State);

  internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit)
    => SchedulingMayChange(battleEvent, unit) || ActionPointsMayChange(battleEvent, unit);
}

public sealed class HasActionPointsCondition(int cost) : UnitActionCondition
{
  public int Cost { get; } = cost;

  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.CurrentActionPoints >= Cost;

  internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit)
    => ActionPointsMayChange(battleEvent, unit);
}

// Weapons without a magazine are always loaded.
public sealed class WeaponIsLoadedCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.EquippedWeapon.Match(
      Some: weapon => weapon.IsLoaded,
      None: () => false);

  internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit)
    => WeaponMayChange(battleEvent, unit);
}

public sealed class CanReloadCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => unit.State.EquippedWeapon.Match(
      Some: weapon => weapon.CanReload(),
      None: () => false);

  internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit)
    => WeaponMayChange(battleEvent, unit);
}

public sealed class IsActiveSideCondition : UnitActionCondition
{
  internal override bool IsMet(BattleSession session, AliveUnit unit)
    => session.Phase == BattlePhase.InProgress && unit.State.Side == session.ActiveSide;

  internal override bool MayChange(BattleEvent battleEvent, BattleUnitState unit)
    => PhaseOrSideMayChange(battleEvent);
}
