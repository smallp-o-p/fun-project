namespace FunProject.Battle;

// Answers "what are the odds of this shot", not "may this unit act right
// now" — phase, turn order, and action points are deliberately not checked
// so the HUD can preview percentages while planning.
public sealed class GetHitChanceForAttack : BattleSessionQuery<HitChanceBreakdown>
{
  public AliveUnit Attacker { get; }
  public AliveUnit Target { get; }

  public GetHitChanceForAttack(AliveUnit attacker, AliveUnit target)
  {
    Attacker = attacker;
    Target = target;
  }

  internal override Either<BattleQueryFailure, HitChanceBreakdown> Execute(BattleSession session)
  {
    // Attacker/target liveness is now guaranteed by the AliveUnit proofs, so this query only
    // resolves target feasibility (weapon/self/ally/visibility/range). AttackFeasibility stays
    // the single source of truth for the ACTION path, which still re-validates at commit time.
    return AttackFeasibility.Resolve(session, Attacker.State, Target.State).Match(
      Left: failure => Fail(MapFailureReason(failure.Kind), failure.Message),
      Right: resolved =>
      {
        AttackContext context = new(Attacker.State, resolved.AttackerPoint, resolved.TargetPoint, session.Board);
        return Succeed(session.HitChanceCalculator.Calculate(context));
      });
  }

  private static BattleQueryFailureReason MapFailureReason(AttackFeasibilityFailureKind kind) => kind switch
  {
    AttackFeasibilityFailureKind.PositionUnresolved => BattleQueryFailureReason.InvalidTile,
    _ => BattleQueryFailureReason.InvalidBattleState,
  };
}
