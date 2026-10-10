using System;
using System.Collections.Generic;
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
    AggressionPercent = Math.Clamp(aggressionPercent, 0, 200);
    CautionPercent = Math.Clamp(cautionPercent, 0, 200);
    RangeWeightPercent = Math.Clamp(rangeWeightPercent, 0, 200);
    PreferredRangePercent = Math.Clamp(preferredRangePercent, 0, 100);
  }

  // Score in points: 60*A*damage + 100*A*kill - 35*C*exposure
  //   + 20*B*rangeImprovement + 20*progress + 8*frontier + 15*reload - 2.
  // A/C/B are percentage weights divided by 100. Features round to hundredths.
  // Keep terms scaled by 10,000, then divide by 100 to return centipoints with
  // one final rounding. Default damage=.25, exposure=.10 gives 9.50 points.
  internal long Score(double damage = 0, double kill = 0, double exposure = 0,
    double rangeImprovement = 0, double progress = 0, double frontier = 0, bool reload = false)
  {
    double damageTerm = 60d * AggressionPercent * Quantize(damage);
    double killTerm = 100d * AggressionPercent * Quantize(kill);
    double exposurePenalty = 35d * CautionPercent * Quantize(exposure);
    double rangeTerm = 20d * RangeWeightPercent * Quantize(rangeImprovement);
    long progressTerm = 20L * 100 * Quantize(progress);
    long frontierTerm = 8L * 100 * Quantize(frontier);
    long reloadTerm = reload ? 15L * 10000 : 0;
    double total = damageTerm + killTerm - exposurePenalty + rangeTerm
      + progressTerm + frontierTerm + reloadTerm - (2L * 10000);

    return (long)Math.Round(total / 100.0, MidpointRounding.AwayFromZero);
  }

  private static int Quantize(double value)
    => (int)Math.Round(Math.Clamp(value, -1, 1) * 100, MidpointRounding.AwayFromZero);
}

internal sealed class EnemyPolicy(EnemyPolicySettings? settings = null)
{
  private readonly EnemyPolicySettings _settings = settings ?? new EnemyPolicySettings();

  public (EnemyIntent Intent, BattleAction Action) DecideAction(BattleRuntime runtime, Faction side, EnemyTurnMemory memory, AliveUnit actor)
  {
    if (actor.State is null)
      return (new EnemyIntent(EnemyIntentKind.EndFactionTurn), BattleAction.EndFactionTurn(side));

    var position = actor.Position.Raw;
    var visible = runtime.Query(new GetVisibleEnemiesForUnit(actor));

    var targets = visible
        .AsValueEnumerable()
        .Where(unit => !unit.State.IsUnconscious)
        .OrderBy(target => Distance(position, target.Position.Raw))
        .ToArray();

    var visibleIds = visible.AsValueEnumerable()
        .Select(unit => unit.Id)
        .ToHashSet();

    var contacts = runtime.Query(new GetFactionLastSeenContacts(side))
      .AsValueEnumerable()
      .Where(contact => !visibleIds.Contains(contact.UnitId));

    foreach (var contact in contacts)
    {
      if (runtime.TryGetTile(contact.Position)
          .Match(tile => runtime.Query(new IsTileVisibleToFaction(side, tile)), () => false))
      {
        memory.Dismiss(contact);
      }
    }

    // Only the actor's cell, candidate destinations and diagonal flanks need terrain.
    Dictionary<Vector3I, TileKnowledge> terrain = [];

    var moves = BattlePathSearch.AdjacentDirections.AsValueEnumerable()
      .Where(direction => !memory.HasVisited(actor.Id, position + direction))
      .Where(LegalNeighbor)
      .Select(direction => position + direction)
      .OrderBy(point => point.X)
      .ThenBy(point => point.Y)
      .ThenBy(point => point.Z)
      .ToArray();

    // Prefer a visible enemy, then the newest unresolved contact, then an adjacent frontier.

    Option<Vector3I> goal = targets.Length > 0 ? Some(targets[0].Position.Raw)
      : contacts
        .Where(contact => !memory.IsDismissed(contact))
        .OrderByDescending(contact => contact.VisibilityRevision)
        .ThenBy(contact => contact.UnitId)
        .Select(contact => Some(contact.Position)).FirstOrDefault();

    Dictionary<Vector3I, int> frontiers = [];

    if (goal.IsNone)
    {
      goal = moves.AsValueEnumerable()
        .Where(point => Frontier(point) > 0)
        .OrderByDescending(Frontier)
        .ThenBy(point => point.X)
        .ThenBy(point => point.Y)
        .ThenBy(point => point.Z)
        .Select(Some)
        .FirstOrDefault();
    }

    var chosen = new EnemyIntent(EnemyIntentKind.Pass, actor.Id);
    BattleAction chosenAction = BattleAction.PassUnit(actor);

    long bestScore = 0;

    // Preview useful damage and kill probability.
    var weaponOpt = actor.State.EquippedWeapon;
    int range = 0;

    if (weaponOpt.Case is not Weapon weapon)
      goto moveScore;

    range = weapon.EffectiveRange;
    if (!weapon.IsLoaded)
    {
      // Reload only an empty magazine; otherwise score single-step legal movement.
      if (actor.State.CurrentActionPoints >= BattleSession.DefaultReloadActionPointCost &&
          weapon is AmmunitionedWeapon { CurrentAmmo: 0 } magazine &&
          magazine.CanReload())
      {
        if (Consider(new EnemyIntent(EnemyIntentKind.Reload, actor.Id),
              _settings.Score(exposure: Exposure(position), reload: true)))
        {
          chosenAction = BattleAction.ReloadWeapon(actor, magazine);
        }
      }
    }

    if (weapon.IsLoaded && actor.State.CurrentActionPoints >= BattleSession.DefaultAttackActionPointCost)
    {
      long healthDmg = 0, stunDmg = 0;
      foreach (var damage in weapon.EmitDamage(actor.State.ActiveBuffDamageMods))
      {
        if (damage.Kind == DamageKind.Health) healthDmg += damage.Amount;
        else if (damage.Kind == DamageKind.Stun) stunDmg += damage.Amount;
      }

      int healthDamage = (int)Math.Min(healthDmg, int.MaxValue), stunDamage = (int)Math.Min(stunDmg, int.MaxValue);
      foreach (var target in targets)
      {
        var targetProof = runtime.TryGetAttackTarget(new BattleEntity.Unit(target.State))
          .Match(value => value, () => throw new InvalidOperationException("A visible living target must be targetable."));

        runtime.Query(new GetHitChanceForAttack(actor, targetProof)).IfRight(chance =>
        {
          double probability = chance.FinalChance / 100d;
          double useful = (long)Math.Min(healthDamage, target.State.CurrentHealth)
                          + Math.Min(stunDamage, Math.Max(0, target.State.CurrentHealth - target.State.CurrentStun));
          double damage = probability * Math.Min(1, useful / Math.Max(1, target.State.CurrentHealth));
          double kill = healthDamage >= target.State.CurrentHealth ? probability : 0;
          if (Consider(new(EnemyIntentKind.Attack, actor.Id, target.Id),
                _settings.Score(damage: damage, kill: kill, exposure: Exposure(position))))
            chosenAction = BattleAction.AttackEntity(actor, targetProof);
        });
      }
    }

  moveScore:
    if (actor.State.CurrentActionPoints < BattleSession.DefaultMovementStepActionPointCost)
      return (chosen, chosenAction);

    foreach (var destination in moves)
    {
      double progress = goal.Match(point => (Distance(position, point) - Distance(destination, point)) / Math.Max(1, Distance(position, point)), () => 0);

      double rangeImprovement = targets.Length == 0 || range <= 0 ? 0 :
        (Math.Abs(Distance(position, targets[0].Position.Raw) - range * (_settings.PreferredRangePercent / 100d))
         - Math.Abs(Distance(destination, targets[0].Position.Raw) - range * (_settings.PreferredRangePercent / 100d))) / Math.Max(1, range);

      double frontier = (Frontier(destination) - Frontier(position)) / 10d;

      bool consideration = Consider(new(EnemyIntentKind.Move, actor.Id, Destination: destination),
        _settings.Score(exposure: Exposure(destination), rangeImprovement: rangeImprovement, progress: progress,
          frontier: frontier));

      if (consideration)
      {
        chosenAction = BattleAction.MoveUnit(actor, [runtime.TryGetTile(destination).Match(value => value,
          () => throw new InvalidOperationException("An observed-clear neighbor must be in bounds."))]);
      }
    }

    return (chosen, chosenAction);

    TileKnowledge Known(BattleBoardState.ValidatedPoint point)
    {
      if (!terrain.TryGetValue(point.Raw, out var known))
        terrain.Add(point.Raw, known = runtime.Query(new GetTileKnowledgeForFaction(side, point)));
      return known;
    }

    bool Clear(Vector3I point) => runtime.TryGetTile(point)
      .Match(tile =>
        {
          var known = Known(tile);
          return known.Terrain
                   .Match(value => value.IsWalkable, () => false) &&
                 known.Occupancy == TraversalKnowledge.Clear;
        },
        () => false);

    bool LegalNeighbor(Vector3I direction) => Clear(position + direction)
                                              && (direction.X == 0 || direction.Z == 0 || Clear(position + new Vector3I(direction.X, 0, 0))
                                                  || Clear(position + new Vector3I(0, 0, direction.Z)));

    bool Consider(EnemyIntent intent, long score)
    {
      if (score <= 0 || memory.HasFailed(intent))
      {
        return false;
      }

      if (score <= bestScore &&
          (score != bestScore || intent.Kind != EnemyIntentKind.Attack || chosen.Kind != EnemyIntentKind.Attack || intent.TargetId >= chosen.TargetId)
         )
      {
        return false;
      }

      chosen = intent;
      bestScore = score;

      return true;
    }

    int Frontier(Vector3I point)
    {
      if (!frontiers.TryGetValue(point, out int count))
      {
        frontiers.Add(point, count = BattlePathSearch.AdjacentDirections.AsValueEnumerable()
          .Count(direction => runtime.TryGetTile(point + direction)
            .Match(tile => !runtime.Query(new HasFactionExploredTile(side, tile)), () => false)));
      }

      return count;
    }

    double Exposure(Vector3I point)
    {
      var cover = TileCover.None;

      var pt = runtime.TryGetTile(point);
      if (pt.Case is BattleBoardState.ValidatedPoint valid)
      {
        cover = Known(valid).Terrain.Match(known => known.Cover, () => TileCover.None);
      }

      double exposure = 0;
      foreach (var target in targets)
      {
        double protection = CoverRules.Applies(cover, CoverRules.GetApproach(target.Position.Raw, point))
          ? Math.Clamp(cover.Amount / 100d, 0, 1) : 0;
        exposure = Math.Max(exposure, (1 - protection) / (1 + Distance(point, target.Position.Raw)));
      }
      return exposure;
    }
  }

  private static double Distance(Vector3I from, Vector3I to) => BattleBoardState.GetGridDistance(from, to);
}

internal enum EnemyIntentKind { Attack, Reload, Move, Pass, EndFactionTurn }
internal sealed record EnemyIntent(EnemyIntentKind Kind, int ActorId = -1, int TargetId = -1, Vector3I Destination = default);
