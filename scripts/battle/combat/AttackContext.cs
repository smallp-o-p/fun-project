using FunProject.Weapons;

namespace FunProject.Battle;

// Everything a feasible attack resolves to: the attacker's weapon plus the attacker/defender
// board positions, packaged as the hit-chance calculator's input. Resolve below is the sole
// production mint site, so a context always describes positions the units actually stood on
// when feasibility was checked.
public sealed record AttackContext(
  BattleUnitState Attacker,
  Weapon Weapon,
  BattleBoardState.ValidatedPoint AttackerPosition,
  BattleBoardState.ValidatedPoint DefenderPosition,
  BattleBoardState Board)
{
  // Single source of truth for attack target-feasibility, shared by the write side
  // (AttackUnit), the read side (GetHitChanceForAttack), and availability
  // (HasAttackableTargetCondition) so the three can never drift. Covers only target
  // feasibility — attacker liveness/incapacitation/AP/phase/turn gating is the caller's job
  // (the action checks mutable actor state; AliveUnit proofs guard the query). Conditions
  // are checked in one fixed order so a shot rejected for multiple reasons surfaces a
  // single, stable failure message.
  internal static Either<string, AttackContext> Resolve(
    BattleSession session, BattleUnitState attacker, BattleUnitState target)
  {
    if (attacker.EquippedWeapon.IsNone)
      return $"{attacker.Combatant.Name} has no equipped weapon.";
    Weapon weapon = attacker.RequireEquippedWeapon();

    if (target == attacker)
      return $"{attacker.Combatant.Name} cannot attack itself.";
    if (target.Side == attacker.Side)
      return $"{attacker.Combatant.Name} cannot attack allied unit {target.Combatant.Name}.";
    if (!target.IsAlive)
      return $"{target.Combatant.Name} is not alive.";
    if (!attacker.VisibleUnits.Contains(target))
      return $"{attacker.Combatant.Name} cannot see {target.Combatant.Name}.";

    BattleBoardState.ValidatedPoint attackerPoint = session.RequireUnitPosition(attacker);
    BattleBoardState.ValidatedPoint targetPoint = session.RequireUnitPosition(target);

    if (BattleSession.GetGridDistance(attackerPoint.Raw, targetPoint.Raw) > weapon.EffectiveRange)
      return $"{target.Combatant.Name} is out of range for {weapon.ItemName}.";

    return new AttackContext(attacker, weapon, attackerPoint, targetPoint, session.Board);
  }
}
