namespace FunProject.Battle;

/// <summary>Trusted read-only calculation. Custom implementations used by worker previews must
/// avoid scene/native operations that require the host thread.</summary>
public interface IHitChanceCalculator
{
  HitChanceBreakdown Calculate(AttackContext context);
}
