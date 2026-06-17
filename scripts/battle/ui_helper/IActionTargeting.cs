using Godot;
using System.Collections.Generic;

namespace FunProject.Battle;

public enum TargetingKind { TilePath, EnemyTarget }

// Per-verb preview the shell renders: TilePath fills Path; EnemyTarget fills HitChance + Target.
public sealed record ActionPreview(
  TargetingKind Kind,
  IReadOnlyList<Vector3I> Path,
  HitChanceBreakdown? HitChance,
  Vector3I? Target);

// Implemented only by TARGETED verbs. Instant verbs (Pass/EndTurn) carry no targeting and are built
// inline by PlayerActionController. Handlers read state only via runtime.Query and build real BattleActions.
public interface IActionTargeting
{
  TargetingKind Kind { get; }
  IReadOnlyCollection<Vector3I> Begin();                          // candidate cells to highlight
  Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target);
  bool CanCommit(Vector3I target);
  BattleAction Build(Vector3I target);
}

public interface NeedsTargeting
{
  public IActionTargeting Targeting(BattleRuntime runtime);
}
