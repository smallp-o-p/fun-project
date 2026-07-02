using FunProject.Battle;

namespace FunProject.Tests;

internal sealed class AlwaysHitCalculator : IHitChanceCalculator
{
  public HitChanceBreakdown Calculate(AttackContext context) => new(100, []);
}
