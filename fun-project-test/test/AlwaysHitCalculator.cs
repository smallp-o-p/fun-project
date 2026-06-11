using FunProject.Battle;

internal sealed class AlwaysHitCalculator : IHitChanceCalculator
{
  public HitChanceBreakdown Calculate(AttackContext context) => new(100, []);
}
