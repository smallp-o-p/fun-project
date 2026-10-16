using FunProject.Battle;
using GdUnit4;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ActionTargetingTest
{
  // 5x1x5 open board. Player "Hero" (armed) at (0,0,0), enemy "Goon" at (3,0,0).
  private static BattleFixture MakeArmedBattle()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var rifle = TestData.MakeWeapon("Rifle");
    return BattleFixture.Started(new Vector3I(5, 1, 5),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hero", player), rifle), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 0)));
  }

  [TestCase(TestName = "MoveTargeting: Begin reachable, Preview path, Build moves the unit")]
  public static void MoveTargetingFlow()
  {
    using var battle = MakeArmedBattle();
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    var move = new MoveTargeting(battle.Runtime, hero.State);
    Assert.Equal(ConfirmMode.Confirm, move.Confirm); // assigned per verb, before any Begin

    move.Begin();
    IReadOnlyCollection<Vector3I> reachable = move.Candidates;
    Assert.True(reachable.AsValueEnumerable().Contains(new Vector3I(2, 0, 1)));

    ActionPreview preview = move.Preview(new Vector3I(2, 0, 1)).RequireRight();
    Assert.True(preview is PathPreview);
    PathPreview pathPreview = (PathPreview)preview;
    Assert.Equal(new Vector3I(0, 0, 0), pathPreview.Path[0]);
    Assert.Equal(new Vector3I(2, 0, 1), pathPreview.Path[^1]);

    Assert.True(move.CanCommit(new Vector3I(2, 0, 1)));
    battle.Runtime.ExecuteAction(move.Build(new Vector3I(2, 0, 1)));
    // The pre-move proof's snapshot is stale after the commit; re-mint to read the new position.
    Assert.Equal(new Vector3I(2, 0, 1), battle.Runtime.TryGetAlive(hero.State).RequireSome().Position.Raw);
  }

  [TestCase(TestName = "AttackTargeting: Begin candidate enemy tile, Preview hit chance, Build attacks")]
  public static void AttackTargetingFlow()
  {
    using var battle = MakeArmedBattle();
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    var attack = new AttackTargeting(battle.Runtime, hero.State);
    Assert.Equal(ConfirmMode.Immediate, attack.Confirm); // assigned per verb, before any Begin

    attack.Begin();
    IReadOnlyCollection<Vector3I> candidates = attack.Candidates;
    Assert.True(candidates.AsValueEnumerable().Contains(new Vector3I(3, 0, 0)));   // the enemy's tile, in range + visible

    ActionPreview preview = attack.Preview(new Vector3I(3, 0, 0)).RequireRight();
    Assert.True(preview is AttackPreview);

    Assert.True(attack.CanCommit(new Vector3I(3, 0, 0)));
    Assert.False(attack.CanCommit(new Vector3I(1, 0, 0)));     // empty tile is not a candidate

    battle.Runtime.ExecuteAction(attack.Build(new Vector3I(3, 0, 0)));
  }

  [TestCase(TestName = "Boxed-in unit: MoveTargeting yields empty candidates and no committable tiles")]
  public static void BoxedInMoveTargetingHasNoCommittableTiles()
  {
    // Default duel positions: player at (4,0,1), enemy at (4,0,4). Wall off every horizontal
    // neighbor of the player; the board is one tile tall, so there is no vertical escape.
    using var battle = BattleFixture.Duel(map: TestData.MakeMapWithWalls(new Vector3I(8, 1, 8),
      new Vector3I(4, 0, 0), new Vector3I(4, 0, 2), new Vector3I(3, 0, 1), new Vector3I(5, 0, 1)));
    var move = new MoveTargeting(battle.Runtime, battle.PlayerUnit);

    move.Begin();
    Assert.Equal(0, move.Candidates.Count);
    Assert.False(move.CanCommit(new Vector3I(4, 0, 4))); // the enemy's tile
    Assert.False(move.CanCommit(new Vector3I(0, 0, 0))); // an open tile
  }

  [TestCase(TestName = "Out-of-range enemy: Attack row stays available with no committable tiles")]
  public static void OutOfRangeEnemyKeepsAttackAvailableWithNoTargets()
  {
    // Knife (range 1) against an enemy across the board: the Attack verb stays usable, but
    // the candidate set is empty and even the enemy's own tile is not committable.
    using var battle = BattleFixture.Duel(
      player: new("Hero", Position: new Vector3I(0, 0, 0), Weapon: TestData.MakeWeapon("Knife", range: 1)),
      enemy: new("Goon", Position: new Vector3I(7, 0, 7)));

    var actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    Assert.True(Assert.ActionRow<AttackActionDefinition>(actions).IsAvailable);
    var targeting = new AttackTargeting(battle.Runtime, battle.PlayerUnit);
    targeting.Begin();
    Assert.Equal(0, targeting.Candidates.Count);
    Assert.False(targeting.CanCommit(new Vector3I(7, 0, 7)));
  }

  [TestCase]
  public static void ObjectTargetingPreviewsBuildsAndRefreshesAfterDestruction()
  {
    var weapon = TestData.MakeWeapon("Rifle", damage: 5);
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Aim: 0, Weapon: weapon),
      configure: setup => setup with
      {
        Objects =
        [
          new ObjectPlacement(TestData.MakeObject("Crate", 5), new Vector3I(3, 0, 2)),
          new ObjectPlacement(TestData.MakeObject(), new Vector3I(2, 0, 2)),
        ],
      });
    var cell = new Vector3I(3, 0, 2);
    var objects = battle.Query(new GetBattleSpecialObjectsQuery());
    var obj = objects.AsValueEnumerable().Single(o => o.Position == cell);
    var scenery = objects.AsValueEnumerable().Single(o => o.Position == new Vector3I(2, 0, 2));
    var targeting = new AttackTargeting(battle.Runtime, battle.PlayerUnit);
    targeting.Begin();
    Assert.True(targeting.Candidates.AsValueEnumerable().Contains(cell));
    Assert.False(targeting.Candidates.AsValueEnumerable().Contains(scenery.Position));
    var preview = (AttackPreview)targeting.Preview(cell).RequireRight();
    Assert.Equal(100, preview.HitChance.FinalChance);
    Assert.True(targeting.CanCommit(cell));
    battle.Submit(targeting.Build(cell));
    Assert.Equal(Some(ObjectStatus.Destroyed), obj.Status);
    Assert.True(targeting.Preview(cell).IsLeft);
    Assert.Throws<InvalidOperationException>(() => targeting.Build(cell));
    targeting.Begin();
    Assert.False(targeting.Candidates.AsValueEnumerable().Contains(cell));
  }

  [TestCase]
  public static void ObjectCandidatesUseCurrentAttackerFeasibility()
  {
    using var battle = BattleFixture.Duel(configure: setup => setup with
    {
      Objects = [new ObjectPlacement(TestData.MakeObject("Crate", 10), new Vector3I(3, 0, 1))],
    });
    var weapon = TestData.MakeWeapon("Rifle");
    var blind = battle.Spawn(TestData.MakeCombatant("Blind", battle.PlayerFaction, vision: 1),
      new Vector3I(0, 0, 0), weapon);
    var shortWeapon = TestData.MakeWeapon("Knife", range: 1);
    var shortRange = battle.Spawn(TestData.MakeCombatant("Short", battle.PlayerFaction),
      new Vector3I(0, 0, 1), shortWeapon);
    var obj = battle.Query(new GetBattleSpecialObjectsQuery()).AsValueEnumerable().Single();
    Assert.True(battle.Query(new GetFactionVisibleObjectsQuery(battle.PlayerFaction)).AsValueEnumerable().Contains(obj));
    foreach (BattleUnitState unit in new[] { blind, shortRange })
    {
      var targeting = new AttackTargeting(battle.Runtime, unit);
      targeting.Begin();
      Assert.False(targeting.Candidates.AsValueEnumerable().Contains(obj.Position));
    }
  }
}
