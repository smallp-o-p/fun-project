using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// This boundary contains values only: no session, board, entity, equipment or proof handles.
public sealed record EnemyOwnUnit(int UnitId, Vector3I Position, int Health, int Stun,
  int ActionPoints, bool CanAct, bool CanAttack, bool CanReload, int Range,
  int HealthDamage, int StunDamage, int Ammo);
public sealed record EnemyHostile(int UnitId, Vector3I Position, int Health, int Stun);
public sealed record EnemyKnownTile(Vector3I Position, TraversalKnowledge Traversal,
  bool Explored, bool Visible, TileCover Cover);
public sealed record EnemyAttackPreview(int ActorId, int TargetId, int HitChancePercent);
public sealed record EnemyDecisionFrame(int TurnNumber, bool IsTerminal,
  IReadOnlyList<EnemyOwnUnit> OwnUnits, IReadOnlyList<EnemyHostile> Hostiles,
  IReadOnlyList<LastSeenContact> Contacts, IReadOnlyList<EnemyKnownTile> Tiles,
  IReadOnlyList<EnemyAttackPreview> AttackPreviews);

public enum EnemyIntentKind { Attack, Reload, Move, Pass, EndFactionTurn }
public sealed record EnemyIntent(EnemyIntentKind Kind, int ActorId = -1, int TargetId = -1, Vector3I Destination = default);
public sealed record EnemyScoreFeatures(int Damage, int Kill, int Exposure, int RangeImprovement,
  int Progress, int Frontier, int Reload)
{
  public static EnemyScoreFeatures FromNormalized(double damage, double kill, double exposure,
    double rangeImprovement, double progress, double frontier, double reload) => new(
      Quantize(damage), Quantize(kill), Quantize(exposure), Quantize(rangeImprovement),
      Quantize(progress), Quantize(frontier), Quantize(reload));

  private static int Quantize(double value) => (int)Math.Round(
    Math.Clamp(value, -1, 1) * 100, MidpointRounding.AwayFromZero);
}
public sealed record EnemyCandidateTrace(EnemyIntent Intent, EnemyScoreFeatures Features, long ScoreCentipoints);
public sealed record EnemyDecisionTrace(EnemyIntent Intent, long ScoreCentipoints,
  IReadOnlyList<EnemyCandidateTrace> Candidates, string FallbackReason);
