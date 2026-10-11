namespace FunProject.Battle;

public interface IHitChanceCalculator
{
  [PreservesProofs]
  HitChanceBreakdown Calculate(AttackContext context);
}
