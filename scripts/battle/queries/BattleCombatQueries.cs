using FunProject.Weapons;
using LanguageExt.UnsafeValueAccess;
using System;

namespace FunProject.Battle;

// Answers "what are the odds of this shot", not "may this unit act right
// now" — phase, turn order, and action points are deliberately not checked
// so the HUD can preview percentages while planning.
public sealed class GetHitChanceForAttack : BattleSessionQuery<HitChanceBreakdown>
{
  public BattleUnitState Attacker { get; }
  public BattleUnitState Target { get; }

  public GetHitChanceForAttack(BattleUnitState attacker, BattleUnitState target)
  {
    ArgumentNullException.ThrowIfNull(attacker);
    ArgumentNullException.ThrowIfNull(target);
    Attacker = attacker;
    Target = target;
  }

  internal override Either<BattleQueryFailure, HitChanceBreakdown> Execute(BattleSession session)
  {
    if (!Attacker.IsAlive)
      return FailUnitNotAlive(Attacker);

    if (Attacker.EquippedWeapon.IsNone)
      return Fail(BattleQueryFailureReason.InvalidBattleState, $"{Attacker.Combatant.Name} has no equipped weapon.");
    Weapon weapon = Attacker.RequireEquippedWeapon();

    // AttackUnit rejects allied targets (and self, which shares the
    // attacker's side), so those shots have no defined odds to preview.
    if (Target.Side == Attacker.Side)
      return Fail(BattleQueryFailureReason.InvalidBattleState, $"{Attacker.Combatant.Name} cannot attack allied unit {Target.Combatant.Name}.");

    if (!Target.IsAlive)
      return FailUnitNotAlive(Target);

    Option<BattleBoardState.ValidatedPoint> attackerPointOption = session.GetUnitPosition(Attacker);
    if (attackerPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Attacker.Id} is not on a valid tile.");
    Option<BattleBoardState.ValidatedPoint> targetPointOption = session.GetUnitPosition(Target);
    if (targetPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Target.Id} is not on a valid tile.");
    BattleBoardState.ValidatedPoint attackerPoint = attackerPointOption.Value();
    BattleBoardState.ValidatedPoint targetPoint = targetPointOption.Value();

    if (!Attacker.VisibleUnits.Contains(Target))
      return Fail(BattleQueryFailureReason.InvalidBattleState, $"{Attacker.Combatant.Name} cannot see {Target.Combatant.Name}.");
    if (BattleSession.GetGridDistance(attackerPoint.Raw, targetPoint.Raw) > weapon.GetRangeStat().BaseValue)
      return Fail(BattleQueryFailureReason.InvalidBattleState, $"{Target.Combatant.Name} is out of range for {weapon.ItemName}.");

    AttackContext context = new(Attacker, Target, attackerPoint, targetPoint, weapon, session.Board);
    return Succeed(session.HitChanceCalculator.Calculate(context));
  }
}
