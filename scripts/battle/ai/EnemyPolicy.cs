using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed record EnemyPolicySettings
{
  public float AggressionPercent { get; }
  public float CautionPercent { get; }
  public float RangeWeightPercent { get; }
  public float PreferredRangePercent { get; }
  public EnemyPolicySettings(float aggressionPercent = 100, float cautionPercent = 100,
    float rangeWeightPercent = 100, float preferredRangePercent = 75)
  {
    AggressionPercent = Clamp(aggressionPercent, 200, 100);
    CautionPercent = Clamp(cautionPercent, 200, 100);
    RangeWeightPercent = Clamp(rangeWeightPercent, 200, 100);
    PreferredRangePercent = Clamp(preferredRangePercent, 100, 75);
  }

  private static float Clamp(float value, float maximum, float defaultValue)
    => float.IsFinite(value) ? Math.Clamp(value, 0, maximum) : defaultValue;
}

// Pure: permitted detached values and activation memory in, detached decision/trace out.
public sealed class EnemyPolicy(EnemyPolicySettings? settings = null)
{
  private readonly EnemyPolicySettings _settings = settings ?? new();

  // Features are hundredths. Promote float percentages before multiplication and round
  // only the final centipoints, so valid fractional settings keep their precision.
  public long Score(EnemyScoreFeatures features)
  {
    double numerator = 60d * _settings.AggressionPercent * features.Damage
      + 100d * _settings.AggressionPercent * features.Kill
      - 35d * _settings.CautionPercent * features.Exposure
      + 20d * _settings.RangeWeightPercent * features.RangeImprovement
      + 2000L * features.Progress + 800L * features.Frontier + 1500L * features.Reload - 20000;
    return (long)Math.Round(numerator / 100, MidpointRounding.AwayFromZero);
  }

  public EnemyDecisionTrace Decide(EnemyDecisionFrame frame, EnemyTurnMemory memory)
  {
    ArgumentNullException.ThrowIfNull(frame);
    ArgumentNullException.ThrowIfNull(memory);
    var actor = frame.OwnUnits.AsValueEnumerable().Where(unit => unit.CanAct).OrderBy(unit => unit.UnitId).FirstOrDefault();
    if (frame.IsTerminal || actor is null)
      return new(new(EnemyIntentKind.EndFactionTurn), 0, []);
    var targets = frame.Hostiles.AsValueEnumerable().OrderBy(target => Distance(actor.Position, target.Position))
      .ThenBy(target => target.UnitId).Take(8).ToArray();
    var tiles = frame.Tiles.AsValueEnumerable().ToDictionary(tile => tile.Position);
    var moves = frame.Tiles.AsValueEnumerable().Where(tile => LegalNeighbor(actor, tile, tiles)
      && !memory.HasVisited(actor.UnitId, tile.Position))
      .OrderBy(tile => tile.Position.X).ThenBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.Z).ToArray();
    Option<Vector3I> goal = targets.Length > 0 ? Some(targets[0].Position)
      : frame.Contacts.AsValueEnumerable().Where(contact => !memory.IsDismissed(contact))
        .OrderByDescending(contact => contact.VisibilityRevision).ThenBy(contact => contact.UnitId)
        .Select(contact => Some(contact.Position)).FirstOrDefault();
    if (goal.IsNone)
      goal = moves.AsValueEnumerable().Where(tile => Frontier(tile.Position, tiles) > 0)
        .OrderByDescending(tile => Frontier(tile.Position, tiles)).ThenBy(tile => tile.Position.X)
        .ThenBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.Z)
        .Select(tile => Some(tile.Position)).FirstOrDefault();
    List<EnemyCandidateTrace> candidates = [];
    void Add(EnemyIntent intent, EnemyScoreFeatures features)
    {
      if (!memory.HasFailed(intent)) candidates.Add(new(intent, features, Score(features)));
    }
    if (actor.CanAttack)
      foreach (var target in targets)
      {
        var preview = frame.AttackPreviews.AsValueEnumerable().FirstOrDefault(value => value.ActorId == actor.UnitId && value.TargetId == target.UnitId);
        if (preview is null) continue;
        double probability = preview.HitChancePercent / 100d;
        double useful = (long)Math.Min(actor.HealthDamage, target.Health) + Math.Min(actor.StunDamage, Math.Max(0, target.Health - target.Stun));
        double damage = probability * Math.Min(1, useful / Math.Max(1, target.Health));
        double kill = actor.HealthDamage >= target.Health ? probability : 0;
        Add(new(EnemyIntentKind.Attack, actor.UnitId, target.UnitId), EnemyScoreFeatures.FromNormalized(
          damage, kill, Exposure(actor.Position, targets, tiles), 0, 0, 0, 0));
      }
    if (actor.CanReload && actor.Ammo == 0)
      Add(new(EnemyIntentKind.Reload, actor.UnitId), EnemyScoreFeatures.FromNormalized(
        0, 0, Exposure(actor.Position, targets, tiles), 0, 0, 0, 1));
    if (actor.ActionPoints >= BattleSession.DefaultMovementStepActionPointCost)
      foreach (var destination in moves)
      {
        double progress = goal.Match(point => (Distance(actor.Position, point) - Distance(destination.Position, point))
          / Math.Max(1, Distance(actor.Position, point)), () => 0);
        double range = targets.Length == 0 || actor.Range <= 0 ? 0 :
          (Math.Abs(Distance(actor.Position, targets[0].Position) - actor.Range * (_settings.PreferredRangePercent / 100d))
          - Math.Abs(Distance(destination.Position, targets[0].Position) - actor.Range * (_settings.PreferredRangePercent / 100d))) / Math.Max(1, actor.Range);
        double frontier = (Frontier(destination.Position, tiles) - Frontier(actor.Position, tiles)) / 10d;
        Add(new(EnemyIntentKind.Move, actor.UnitId, Destination: destination.Position), EnemyScoreFeatures.FromNormalized(
          0, 0, Exposure(destination.Position, targets, tiles), range, progress, frontier, 0));
      }
    var chosen = candidates.AsValueEnumerable().Where(candidate => candidate.ScoreCentipoints > 0)
      .OrderByDescending(candidate => candidate.ScoreCentipoints).ThenBy(candidate => candidate.Intent.Kind)
      .ThenBy(candidate => candidate.Intent.TargetId).ThenBy(candidate => candidate.Intent.Destination.X)
      .ThenBy(candidate => candidate.Intent.Destination.Y).ThenBy(candidate => candidate.Intent.Destination.Z).FirstOrDefault();
    return chosen is null ? new(new(EnemyIntentKind.Pass, actor.UnitId), 0, candidates.AsReadOnly())
      : new(chosen.Intent, chosen.ScoreCentipoints, candidates.AsReadOnly());
  }

  private static bool LegalNeighbor(EnemyOwnUnit actor, EnemyKnownTile tile, Dictionary<Vector3I, EnemyKnownTile> tiles)
  {
    var delta = tile.Position - actor.Position;
    if (!BattlePathSearch.AdjacentDirections.AsValueEnumerable().Contains(delta) || tile.Traversal != TraversalKnowledge.Clear) return false;
    if (delta.X == 0 || delta.Z == 0) return true;
    bool Clear(Vector3I point) => tiles.TryGetValue(point, out var flank) && flank.Traversal == TraversalKnowledge.Clear;
    return Clear(actor.Position + new Vector3I(delta.X, 0, 0)) || Clear(actor.Position + new Vector3I(0, 0, delta.Z));
  }

  private static int Frontier(Vector3I position, Dictionary<Vector3I, EnemyKnownTile> tiles)
    => BattlePathSearch.AdjacentDirections.AsValueEnumerable().Count(direction => tiles.TryGetValue(position + direction, out var tile) && !tile.Explored);

  private static double Exposure(Vector3I position, EnemyHostile[] targets, Dictionary<Vector3I, EnemyKnownTile> tiles)
  {
    double exposure = 0;
    foreach (var target in targets)
    {
      double cover = tiles.TryGetValue(position, out var tile)
        && CoverRules.Applies(tile.Cover, CoverRules.GetApproach(target.Position, position)) ? Math.Clamp(tile.Cover.Amount / 100d, 0, 1) : 0;
      exposure = Math.Max(exposure, (1 - cover) / (1 + Distance(position, target.Position)));
    }
    return exposure;
  }

  private static double Distance(Vector3I from, Vector3I to) => BattleBoardState.GetGridDistance(from, to);
}
