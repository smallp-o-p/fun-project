namespace FunProject.Battle;

// Answers "what are the odds of this shot", not "may this unit act right
// now" — phase, turn order, and action points are deliberately not checked
// so the HUD can preview percentages while planning.
public sealed class GetHitChanceForAttack(AliveUnit attacker, AttackTarget target)
  : IBattleSessionQuery<Either<BattleQueryFailure, HitChanceBreakdown>>
{
  public Either<BattleQueryFailure, HitChanceBreakdown> Execute(BattleSession session)
  {
    // Attacker/target liveness is guaranteed by the proof arguments, so this query only
    // resolves target feasibility (weapon/self/ally/visibility/range). AttackContext.Resolve
    // stays the single source of truth for the ACTION path, which re-validates at commit time.
    return AttackContext.Resolve(session, attacker.State, target)
      .MapLeft(message => new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, message))
      .Map(context => context.CalculateHitChance(target, session.HitChanceCalculator));
  }
}
