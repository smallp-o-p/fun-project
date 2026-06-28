using FunProject.Battle;
using Godot;
using System.Collections.Generic;

public abstract record ActionPreview;
public sealed record PathPreview(IReadOnlyList<Vector3I> Path) : ActionPreview;
public sealed record AttackPreview(HitChanceBreakdown HitChance) : ActionPreview;

// Implemented only by TARGETED verbs. Instant verbs (Pass/EndTurn) carry no targeting and are built
// inline by PlayerActionController. Handlers read state only via runtime.Query and build real BattleActions.
// The handler OWNS its candidate set (Candidates): Begin() computes it and is the single source the
// controller renders from — the controller keeps no duplicate.
public interface IActionTargeting
{
  IReadOnlyCollection<Vector3I> Candidates { get; }              // last-computed candidate cells to highlight
  IReadOnlyCollection<Vector3I> Begin();                         // (re)computes Candidates and returns it
  Either<BattleQueryFailure, ActionPreview> Preview(Vector3I target);
  bool CanCommit(Vector3I target);
  BattleAction Build(Vector3I target);
}

public interface NeedsTargeting
{
  public IActionTargeting Targeting(BattleRuntime runtime);
}
