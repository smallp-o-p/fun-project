namespace FunProject.Battle;

public interface IHitChanceCalculator
{
  HitChanceBreakdown Calculate(AttackContext context);
}
