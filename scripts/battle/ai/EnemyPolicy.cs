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
      + progressTerm + frontierTerm + reloadTerm - 2L * 10000;
    return (long)Math.Round(total / 100, MidpointRounding.AwayFromZero);
  }

  private static int Quantize(double value)
    => (int)Math.Round(Math.Clamp(value, -1, 1) * 100, MidpointRounding.AwayFromZero);
}

internal sealed class EnemyPolicy(EnemyPolicySettings? settings = null)
{
  private readonly EnemyPolicySettings _settings = settings ?? new();

  // Choose the lowest-ID actable unit, then the highest positive attack/reload/move
  // score. Move toward visible enemies, remembered contacts, or an adjacent frontier;
  // observed-clear terrain and turn memory limit candidates. Ties are deterministic.
  // The chosen action uses this decision's proofs and is submitted immediately.
  internal (EnemyIntent Intent, BattleAction Action) Decide(BattleRuntime runtime, Faction side, EnemyTurnMemory memory)
  {
    // Select one actor and observe only its faction's visible targets and remembered contacts.
    var own = runtime.Query(new GetFactionAliveUnits(side));
    foreach (var unit in own) memory.Visit(unit.Id, unit.Position.Raw);
    var actor = own.AsValueEnumerable().Where(unit => runtime.Query(new CanUnitActNow(unit.State)))
      .OrderBy(unit => unit.Id).FirstOrDefault();
    if (actor.State is null) return (new(EnemyIntentKind.EndFactionTurn), BattleAction.EndFactionTurn(side));
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
    // Cache local frontier and geometric exposure measurements for movement scoring.
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
    // Prefer a visible enemy, then the newest unresolved contact, then an adjacent frontier.
    Option<Vector3I> goal = targets.Length > 0 ? Some(targets[0].Position.Raw)
      : contacts.Where(contact => !memory.IsDismissed(contact)).OrderByDescending(contact => contact.VisibilityRevision)
        .ThenBy(contact => contact.UnitId).Select(contact => Some(contact.Position)).FirstOrDefault();
    if (goal.IsNone)
      goal = moves.AsValueEnumerable().Where(point => Frontier(point) > 0).OrderByDescending(Frontier)
        .ThenBy(point => point.X).ThenBy(point => point.Y).ThenBy(point => point.Z).Select(Some).FirstOrDefault();
    var chosen = new EnemyIntent(EnemyIntentKind.Pass, actor.Id);
    BattleAction chosenAction = BattleAction.PassUnit(actor);
    long bestScore = 0;
    // Attacks, reload, then XYZ-sorted moves establish kind/destination tie order.
    // Targets are distance-sorted for the nearest-eight bound, so ties still need ID.
    bool Consider(EnemyIntent intent, long score)
    {
      if (score <= 0 || memory.HasFailed(intent)) return false;
      if (score > bestScore || score == bestScore && intent.Kind == EnemyIntentKind.Attack
        && chosen.Kind == EnemyIntentKind.Attack && intent.TargetId < chosen.TargetId)
      {
        chosen = intent;
        bestScore = score;
        return true;
      }
      return false;
    }
    // Preview useful damage and kill probability without spending ammunition or RNG.
    var weapon = actor.State.EquippedWeapon;
    if (actor.State.CurrentActionPoints >= BattleSession.DefaultAttackActionPointCost && weapon.Match(value => value.IsLoaded, () => false))
    {
      long health = 0, stun = 0;
      foreach (var damage in weapon.Match(value => value.EmitDamage(actor.State.ActiveBuffDamageMods), () => []))
        if (damage.Kind == DamageKind.Health) health += damage.Amount;
        else if (damage.Kind == DamageKind.Stun) stun += damage.Amount;
      int healthDamage = (int)Math.Min(health, int.MaxValue), stunDamage = (int)Math.Min(stun, int.MaxValue);
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
    // Reload only an empty magazine; otherwise score single-step legal movement.
    if (actor.State.CurrentActionPoints >= BattleSession.DefaultReloadActionPointCost)
      weapon.IfSome(equipped =>
      {
        if (equipped is AmmunitionedWeapon magazine && magazine.CurrentAmmo == 0 && magazine.CanReload())
          if (Consider(new(EnemyIntentKind.Reload, actor.Id), _settings.Score(exposure: Exposure(position), reload: true)))
            chosenAction = BattleAction.ReloadWeapon(actor, magazine);
      });
    int range = weapon.Match(value => value.EffectiveRange, () => 0);
    if (actor.State.CurrentActionPoints >= BattleSession.DefaultMovementStepActionPointCost)
      foreach (var destination in moves)
      {
        double progress = goal.Match(point => (Distance(position, point) - Distance(destination, point)) / Math.Max(1, Distance(position, point)), () => 0);
        double rangeImprovement = targets.Length == 0 || range <= 0 ? 0 :
          (Math.Abs(Distance(position, targets[0].Position.Raw) - range * (_settings.PreferredRangePercent / 100d))
          - Math.Abs(Distance(destination, targets[0].Position.Raw) - range * (_settings.PreferredRangePercent / 100d))) / Math.Max(1, range);
        double frontier = (Frontier(destination) - Frontier(position)) / 10d;
        if (Consider(new(EnemyIntentKind.Move, actor.Id, Destination: destination),
          _settings.Score(exposure: Exposure(destination), rangeImprovement: rangeImprovement, progress: progress, frontier: frontier)))
          chosenAction = BattleAction.MoveUnit(actor, [runtime.TryGetTile(destination).Match(value => value,
            () => throw new InvalidOperationException("An observed-clear neighbor must be in bounds."))]);
      }
    return (chosen, chosenAction);
  }

  private static double Distance(Vector3I from, Vector3I to) => BattleBoardState.GetGridDistance(from, to);
}

internal enum EnemyIntentKind { Attack, Reload, Move, Pass, EndFactionTurn }
internal sealed record EnemyIntent(EnemyIntentKind Kind, int ActorId = -1, int TargetId = -1, Vector3I Destination = default);
