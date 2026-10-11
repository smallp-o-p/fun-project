using FunProject.Weapons;
using System;

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
  // (AttackEntity) and the read side (GetHitChanceForAttack) so the two can never drift.
  // Covers only target feasibility — attacker liveness/incapacitation/AP/phase/turn
  // gating is the caller's job (the action checks mutable actor state; AliveUnit proofs
  // guard the query). Conditions are checked in one fixed order so a shot rejected for
  // multiple reasons surfaces a single, stable failure message.
  internal static Either<string, AttackContext> Resolve(
    BattleSession session, AliveUnit attackerProof, AttackTarget target)
  {
    var attacker = attackerProof.State;
    if (attacker.EquippedWeapon.IsNone)
      return $"{attacker.Combatant.Name} has no equipped weapon.";
    Weapon weapon = attacker.RequireEquippedWeapon();

    string name;
    switch (target.Entity)
    {
      case BattleEntity.Unit unit:
        name = unit.State.Combatant.Name;
        if (unit.State == attacker) return $"{name} cannot attack itself.";
        if (unit.State.Side == attacker.Side)
          return $"{attacker.Combatant.Name} cannot attack allied unit {name}.";
        break;
      case BattleEntity.BoardObject obj:
        name = obj.State.Name;
        break;
      default:
        throw new InvalidOperationException("An attack requires a typed target.");
    }

    bool visible = target.Entity switch
    {
      BattleEntity.Unit unit => attacker.VisibleUnits.Contains(unit.State),
      BattleEntity.BoardObject => attacker.VisibleTiles.Contains(target.Position),
      _ => throw new InvalidOperationException("Unknown battle entity."),
    };
    if (!visible) return $"{attacker.Combatant.Name} cannot see {name}.";
    if (BattleSession.GetGridDistance(attackerProof.Position.Raw, target.Position.Raw) > weapon.EffectiveRange)
      return $"{name} is out of range for {weapon.ItemName}.";
    return new AttackContext(attacker, weapon, attackerProof.Position, target.Position, session.Board);
  }

  // Object shots bypass the unit calculator entirely: a destructible object has no aim,
  // cover, or dodge profile, so a feasible shot always connects.
  internal HitChanceBreakdown CalculateHitChance(AttackTarget target, IHitChanceCalculator calculator) =>
    target.Entity is BattleEntity.BoardObject ? new HitChanceBreakdown(100, []) : calculator.Calculate(this);
}
