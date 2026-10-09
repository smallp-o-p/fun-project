using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Weapons;

namespace FunProject.Battle;

// The only live-to-detached boundary. Hostile equipment and hidden current state never
// enter the frame; remembered terrain/contacts retain their last permitted observation.
public sealed class GetEnemyDecisionFrame(Faction faction) : IBattleSessionQuery<EnemyDecisionFrame>
{
  public EnemyDecisionFrame Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(faction);
    var knowledge = new GetFactionUnitKnowledge(faction).Execute(session);
    var hostiles = knowledge.Current.AsValueEnumerable()
      .Where(unit => !unit.IsOwnSide && !unit.IsUnconscious)
      .Select(unit => new EnemyHostile(unit.UnitId, unit.Position, unit.Health,
        session.AliveUnits.AsValueEnumerable().Single(live => live.Id == unit.UnitId).CurrentStun))
      .ToArray();
    List<EnemyOwnUnit> own = [];
    List<EnemyAttackPreview> previews = [];
    bool actorChosen = false;
    foreach (var unit in session.GetFactionAliveUnits(faction).AsValueEnumerable().OrderBy(unit => unit.Id))
    {
      var alive = session.MintAlive(unit);
      var weapon = unit.EquippedWeapon;
      var bundle = weapon.Match(value => value.EmitDamage(unit.ActiveBuffDamageMods), () => []);
      long health = 0, stun = 0;
      foreach (var damage in bundle)
        if (damage.Kind == DamageKind.Health) health += damage.Amount;
        else if (damage.Kind == DamageKind.Stun) stun += damage.Amount;
      bool canAct = session.CanUnitActNow(unit);
      bool chosenActor = canAct && !actorChosen;
      actorChosen |= canAct;
      bool canAttack = canAct && unit.CurrentActionPoints >= BattleSession.DefaultAttackActionPointCost
        && weapon.Match(value => value.IsLoaded, () => false);
      bool canReload = canAct && unit.CurrentActionPoints >= BattleSession.DefaultReloadActionPointCost
        && weapon.Match(value => value is AmmunitionedWeapon magazine && magazine.CurrentAmmo == 0 && magazine.CanReload(), () => false);
      own.Add(new(unit.Id, alive.Position.Raw, unit.CurrentHealth, unit.CurrentStun,
        unit.CurrentActionPoints, canAct, canAttack, canReload,
        weapon.Match(value => value.EffectiveRange, () => 0),
        (int)Math.Min(health, int.MaxValue), (int)Math.Min(stun, int.MaxValue),
        weapon.Match(value => value is AmmunitionedWeapon magazine ? magazine.CurrentAmmo : -1, () => -1)));
      // The policy always uses the lowest-ID actable actor, including an unarmed one.
      if (!chosenActor || !canAttack) continue;
      foreach (var hostile in hostiles.AsValueEnumerable()
        .OrderBy(target => BattleSession.GetGridDistance(alive.Position.Raw, target.Position))
        .ThenBy(target => target.UnitId).Take(8))
      {
        var target = session.AliveUnits.AsValueEnumerable().Single(live => live.Id == hostile.UnitId);
        new GetHitChanceForAttack(alive, session.TryGetAttackTarget(new BattleEntity.Unit(target)).Match(
          value => value, () => throw new InvalidOperationException("A visible living unit must be targetable.")))
          .Execute(session).IfRight(chance => previews.Add(new(unit.Id, hostile.UnitId, chance.FinalChance)));
      }
    }

    List<EnemyKnownTile> tiles = [];
    var explored = session.GetFactionExploredTiles(faction);
    var visible = session.GetFactionVisibleTiles(faction);
    var dimensions = session.Board.Dimensions;
    for (int x = 0; x < dimensions.X; x++)
      for (int y = 0; y < dimensions.Y; y++)
        for (int z = 0; z < dimensions.Z; z++)
        {
          var point = session.Board.ValidatePoint(new(x, y, z)).Match(value => value,
            () => throw new InvalidOperationException("Board dimensions must describe valid tiles."));
          var known = new GetTileKnowledgeForFaction(faction, point).Execute(session);
          var traversal = known.Terrain.Match(value => !value.IsWalkable, () => false)
            || known.Occupancy == TraversalKnowledge.Blocked ? TraversalKnowledge.Blocked
            : known.Terrain.IsSome && known.Occupancy == TraversalKnowledge.Clear ? TraversalKnowledge.Clear : TraversalKnowledge.Unknown;
          tiles.Add(new(point.Raw, traversal, explored.Contains(point), visible.Contains(point),
            known.Terrain.Match(value => value.Cover, () => TileCover.None)));
        }
    return new(session.TurnNumber, session.Outcome.IsSome, own.AsReadOnly(),
      System.Array.AsReadOnly(hostiles), knowledge.Remembered, tiles.AsReadOnly(), previews.AsReadOnly());
  }
}
