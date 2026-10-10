using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FunProject.Combatants;
using FunProject.Weapons;

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

  // Candidate measurements round to hundredths; float weights keep their precision
  // until the final centipoint rounding. The unweighted terms use long intermediates.
  internal long Score(double damage = 0, double kill = 0, double exposure = 0,
    double rangeImprovement = 0, double progress = 0, double frontier = 0, bool reload = false)
  {
    double numerator = 60d * AggressionPercent * Quantize(damage)
      + 100d * AggressionPercent * Quantize(kill)
      - 35d * CautionPercent * Quantize(exposure)
      + 20d * RangeWeightPercent * Quantize(rangeImprovement)
      + 2000L * Quantize(progress) + 800L * Quantize(frontier) + (reload ? 150000L : 0) - 20000;
    return (long)Math.Round(numerator / 100, MidpointRounding.AwayFromZero);
  }

  private static int Quantize(double value)
    => (int)Math.Round(Math.Clamp(value, -1, 1) * 100, MidpointRounding.AwayFromZero);
}

internal sealed class EnemyPolicy(EnemyPolicySettings? settings = null)
{
  private readonly EnemyPolicySettings _settings = settings ?? new();

  internal EnemyIntent Decide(BattleRuntime runtime, Faction side, EnemyTurnMemory memory)
  {
    var own = runtime.Query(new GetFactionAliveUnits(side));
    foreach (var unit in own) memory.Visit(unit.Id, unit.Position.Raw);
    var actor = own.AsValueEnumerable().Where(unit => runtime.Query(new CanUnitActNow(unit.State)))
      .OrderBy(unit => unit.Id).FirstOrDefault();
    if (actor.State is null) return new(EnemyIntentKind.EndFactionTurn);
    var position = actor.Position.Raw;
    var visible = runtime.Query(new GetVisibleHostileUnitsForFaction(side));
    var targets = visible.AsValueEnumerable().Where(unit => !unit.State.IsUnconscious)
      .OrderBy(target => Distance(position, target.Position.Raw)).ThenBy(target => target.Id).Take(8).ToArray();
    var visibleIds = visible.AsValueEnumerable().Select(unit => unit.Id).ToHashSet();
    var contacts = runtime.Query(new GetFactionLastSeenContacts(side)).AsValueEnumerable()
      .Where(contact => !visibleIds.Contains(contact.UnitId));
    foreach (var contact in contacts)
      if (runtime.TryGetTile(contact.Position).Match(tile => runtime.Query(new IsTileVisibleToFaction(side, tile)), () => false))
        memory.Dismiss(contact);

    // Only the actor's cell, candidate destinations and diagonal flanks need terrain.
    Dictionary<Vector3I, TileKnowledge> terrain = [];
    TileKnowledge Known(BattleBoardState.ValidatedPoint point)
    {
      if (!terrain.TryGetValue(point.Raw, out var known))
        terrain.Add(point.Raw, known = runtime.Query(new GetTileKnowledgeForFaction(side, point)));
      return known;
    }
    bool Clear(Vector3I point) => runtime.TryGetTile(point).Match(tile =>
    {
      var known = Known(tile);
      return known.Terrain.Match(value => value.IsWalkable, () => false) && known.Occupancy == TraversalKnowledge.Clear;
    }, () => false);
    bool LegalNeighbor(Vector3I direction) => Clear(position + direction)
      && (direction.X == 0 || direction.Z == 0 || Clear(position + new Vector3I(direction.X, 0, 0))
        || Clear(position + new Vector3I(0, 0, direction.Z)));
    var moves = BattlePathSearch.AdjacentDirections.AsValueEnumerable()
      .Where(direction => !memory.HasVisited(actor.Id, position + direction)).Where(LegalNeighbor)
      .Select(direction => position + direction)
      .OrderBy(point => point.X).ThenBy(point => point.Y).ThenBy(point => point.Z).ToArray();
    Dictionary<Vector3I, int> frontiers = [];
    int Frontier(Vector3I point)
    {
      if (!frontiers.TryGetValue(point, out int count))
        frontiers.Add(point, count = BattlePathSearch.AdjacentDirections.AsValueEnumerable().Count(direction =>
          runtime.TryGetTile(point + direction).Match(tile => !runtime.Query(new HasFactionExploredTile(side, tile)), () => false)));
      return count;
    }
    double Exposure(Vector3I point)
    {
      if (targets.Length == 0) return 0;
      var cover = runtime.TryGetTile(point).Match(tile => Known(tile).Terrain.Match(known => known.Cover, () => TileCover.None),
        () => TileCover.None);
      double exposure = 0;
      foreach (var target in targets)
      {
        double protection = CoverRules.Applies(cover, CoverRules.GetApproach(target.Position.Raw, point))
          ? Math.Clamp(cover.Amount / 100d, 0, 1) : 0;
        exposure = Math.Max(exposure, (1 - protection) / (1 + Distance(point, target.Position.Raw)));
      }
      return exposure;
    }
    Option<Vector3I> goal = targets.Length > 0 ? Some(targets[0].Position.Raw)
      : contacts.Where(contact => !memory.IsDismissed(contact)).OrderByDescending(contact => contact.VisibilityRevision)
        .ThenBy(contact => contact.UnitId).Select(contact => Some(contact.Position)).FirstOrDefault();
    if (goal.IsNone)
      goal = moves.AsValueEnumerable().Where(point => Frontier(point) > 0).OrderByDescending(Frontier)
        .ThenBy(point => point.X).ThenBy(point => point.Y).ThenBy(point => point.Z).Select(Some).FirstOrDefault();
    var chosen = new EnemyIntent(EnemyIntentKind.Pass, actor.Id);
    long bestScore = 0;
    // Attacks, reload, then XYZ-sorted moves establish kind/destination tie order.
    // Targets are distance-sorted for the nearest-eight bound, so ties still need ID.
    void Consider(EnemyIntent intent, long score)
    {
      if (score <= 0 || memory.HasFailed(intent)) return;
      if (score > bestScore || score == bestScore && intent.Kind == EnemyIntentKind.Attack
        && chosen.Kind == EnemyIntentKind.Attack && intent.TargetId < chosen.TargetId)
      {
        chosen = intent;
        bestScore = score;
      }
    }
    var weapon = actor.State.EquippedWeapon;
    if (actor.State.CurrentActionPoints >= BattleSession.DefaultAttackActionPointCost && weapon.Match(value => value.IsLoaded, () => false))
    {
      long health = 0, stun = 0;
      foreach (var damage in weapon.Match(value => value.EmitDamage(actor.State.ActiveBuffDamageMods), () => []))
        if (damage.Kind == DamageKind.Health) health += damage.Amount;
        else if (damage.Kind == DamageKind.Stun) stun += damage.Amount;
      int healthDamage = (int)Math.Min(health, int.MaxValue), stunDamage = (int)Math.Min(stun, int.MaxValue);
      foreach (var target in targets)
        runtime.Query(new GetHitChanceForAttack(actor, runtime.TryGetAttackTarget(new BattleEntity.Unit(target.State))
          .Match(value => value, () => throw new InvalidOperationException("A visible living target must be targetable."))))
          .IfRight(chance =>
          {
            double probability = chance.FinalChance / 100d;
            double useful = (long)Math.Min(healthDamage, target.State.CurrentHealth)
              + Math.Min(stunDamage, Math.Max(0, target.State.CurrentHealth - target.State.CurrentStun));
            double damage = probability * Math.Min(1, useful / Math.Max(1, target.State.CurrentHealth));
            double kill = healthDamage >= target.State.CurrentHealth ? probability : 0;
            Consider(new(EnemyIntentKind.Attack, actor.Id, target.Id), _settings.Score(damage: damage, kill: kill, exposure: Exposure(position)));
          });
    }
    if (actor.State.CurrentActionPoints >= BattleSession.DefaultReloadActionPointCost
      && weapon.Match(value => value is AmmunitionedWeapon magazine && magazine.CurrentAmmo == 0 && magazine.CanReload(), () => false))
      Consider(new(EnemyIntentKind.Reload, actor.Id), _settings.Score(exposure: Exposure(position), reload: true));
    int range = weapon.Match(value => value.EffectiveRange, () => 0);
    if (actor.State.CurrentActionPoints >= BattleSession.DefaultMovementStepActionPointCost)
      foreach (var destination in moves)
      {
        double progress = goal.Match(point => (Distance(position, point) - Distance(destination, point)) / Math.Max(1, Distance(position, point)), () => 0);
        double rangeImprovement = targets.Length == 0 || range <= 0 ? 0 :
          (Math.Abs(Distance(position, targets[0].Position.Raw) - range * (_settings.PreferredRangePercent / 100d))
          - Math.Abs(Distance(destination, targets[0].Position.Raw) - range * (_settings.PreferredRangePercent / 100d))) / Math.Max(1, range);
        double frontier = (Frontier(destination) - Frontier(position)) / 10d;
        Consider(new(EnemyIntentKind.Move, actor.Id, Destination: destination),
          _settings.Score(exposure: Exposure(destination), rangeImprovement: rangeImprovement, progress: progress, frontier: frontier));
      }
    return chosen;
  }

  private static double Distance(Vector3I from, Vector3I to) => BattleBoardState.GetGridDistance(from, to);
}

internal enum EnemyIntentKind { Attack, Reload, Move, Pass, EndFactionTurn }
[StructLayout(LayoutKind.Auto)]
internal readonly record struct EnemyIntent(EnemyIntentKind Kind, int ActorId = -1, int TargetId = -1, Vector3I Destination = default);
