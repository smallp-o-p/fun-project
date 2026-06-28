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
    // Attacker liveness is the read side's own gate (it deliberately skips the
    // phase/turn/AP checks AttackUnit gets from ValidateActingUnit), so it must
    // run before the shared target-feasibility resolver. A dead attacker has no
    // odds even against an ally.
    if (!Attacker.IsAlive)
      return FailUnitNotAlive(Attacker);

    return AttackFeasibility.Resolve(session, Attacker, Target).Match(
      Left: failure => Fail(MapFailureReason(failure.Kind), failure.Message),
      Right: resolved =>
      {
        AttackContext context = new(Attacker, resolved.AttackerPoint, resolved.TargetPoint, session.Board);
        return Succeed(session.HitChanceCalculator.Calculate(context));
      });
  }

  private static BattleQueryFailureReason MapFailureReason(AttackFeasibilityFailureKind kind) => kind switch
  {
    AttackFeasibilityFailureKind.TargetNotAlive => BattleQueryFailureReason.UnitNotAlive,
    AttackFeasibilityFailureKind.PositionUnresolved => BattleQueryFailureReason.InvalidTile,
    _ => BattleQueryFailureReason.InvalidBattleState,
  };
}
