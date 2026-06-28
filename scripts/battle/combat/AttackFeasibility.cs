using FunProject.Weapons;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Battle;

// Neutral classification of why an attack is not feasible. The write side
// (AttackUnit) and the read side (GetHitChanceForAttack) each map these kinds
// onto their own failure-reason vocabulary, so the resolver stays oblivious to
// both BattleActionFailureReason and BattleQueryFailureReason.
internal enum AttackFeasibilityFailureKind
{
  NoEquippedWeapon,
  SelfTarget,
  AlliedTarget,
  TargetNotAlive,
  TargetNotVisible,
  PositionUnresolved,
  OutOfRange,
}

internal sealed record AttackFeasibilityFailure(AttackFeasibilityFailureKind Kind, string Message);

// The data both callers need once an attack is known to be feasible: the
// attacker's weapon plus the resolved attacker/target board positions.
internal sealed record ResolvedAttack(
  Weapon Weapon,
  BattleBoardState.ValidatedPoint AttackerPoint,
  BattleBoardState.ValidatedPoint TargetPoint);

// Single source of truth for attack target-feasibility. Covers only target
// feasibility — attacker liveness/AP/phase/turn/immobilized gating is the
// caller's job (ValidateActingUnit on the write side, an explicit liveness
// check on the read side). Conditions are checked in one fixed order so a shot
// rejected for multiple reasons surfaces a single, stable failure.
internal static class AttackFeasibility
{
  internal static Either<AttackFeasibilityFailure, ResolvedAttack> Resolve(
    BattleSession session, BattleUnitState attacker, BattleUnitState target)
  {
    if (attacker.EquippedWeapon.IsNone)
      return Fail(AttackFeasibilityFailureKind.NoEquippedWeapon,
        $"{attacker.Combatant.Name} has no equipped weapon.");
    Weapon weapon = attacker.RequireEquippedWeapon();

    if (target == attacker)
      return Fail(AttackFeasibilityFailureKind.SelfTarget,
        $"{attacker.Combatant.Name} cannot attack itself.");
    if (target.Side == attacker.Side)
      return Fail(AttackFeasibilityFailureKind.AlliedTarget,
        $"{attacker.Combatant.Name} cannot attack allied unit {target.Combatant.Name}.");
    if (!target.IsAlive)
      return Fail(AttackFeasibilityFailureKind.TargetNotAlive,
        $"{target.Combatant.Name} is not alive.");
    if (!attacker.VisibleUnits.Contains(target))
      return Fail(AttackFeasibilityFailureKind.TargetNotVisible,
        $"{attacker.Combatant.Name} cannot see {target.Combatant.Name}.");

    Option<BattleBoardState.ValidatedPoint> attackerPointOption = session.GetUnitPosition(attacker);
    if (attackerPointOption.IsNone)
      return Fail(AttackFeasibilityFailureKind.PositionUnresolved,
        $"Unit {attacker.Id} is not on the board.");
    Option<BattleBoardState.ValidatedPoint> targetPointOption = session.GetUnitPosition(target);
    if (targetPointOption.IsNone)
      return Fail(AttackFeasibilityFailureKind.PositionUnresolved,
        $"Unit {target.Id} is not on the board.");
    BattleBoardState.ValidatedPoint attackerPoint = attackerPointOption.Value();
    BattleBoardState.ValidatedPoint targetPoint = targetPointOption.Value();

    if (BattleSession.GetGridDistance(attackerPoint.Raw, targetPoint.Raw) > weapon.EffectiveRange)
      return Fail(AttackFeasibilityFailureKind.OutOfRange,
        $"{target.Combatant.Name} is out of range for {weapon.ItemName}.");

    return Right<AttackFeasibilityFailure, ResolvedAttack>(
      new ResolvedAttack(weapon, attackerPoint, targetPoint));
  }

  private static Either<AttackFeasibilityFailure, ResolvedAttack> Fail(
    AttackFeasibilityFailureKind kind, string message) =>
    Left<AttackFeasibilityFailure, ResolvedAttack>(new AttackFeasibilityFailure(kind, message));
}
