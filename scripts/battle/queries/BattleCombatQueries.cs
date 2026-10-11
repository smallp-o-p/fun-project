namespace FunProject.Battle;

// Answers "what are the odds of this shot", not "may this unit act right
// now" — phase, turn order, and action points are deliberately not checked
// so the HUD can preview percentages while planning.
public sealed class GetHitChanceForAttack(AliveUnit attacker, AttackTarget target)
  : IBattleSessionQuery<Either<BattleQueryFailure, HitChanceBreakdown>>
{
  public Either<BattleQueryFailure, HitChanceBreakdown> Execute(BattleSession session)
  {
    // Freshness does not establish membership in this query's session. Validate at
    // this public boundary, then share the mutation-free feasibility logic with actions.
    if (session.TryGetAlive(attacker.State).Case is not AliveUnit sessionAttacker)
      return new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState,
        $"{attacker.State.Combatant.Name} is not alive in this session.");
    if (session.TryGetAttackTarget(target.Entity).Case is not AttackTarget sessionTarget)
      return new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState,
        "Target is not alive or attackable in this session.");

    return AttackContext.Resolve(session, sessionAttacker, sessionTarget)
      .MapLeft(message => new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, message))
      .Map(context => context.CalculateHitChance(sessionTarget, session.HitChanceCalculator));
  }
}
