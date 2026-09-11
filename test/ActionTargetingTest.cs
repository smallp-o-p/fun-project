using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class ActionTargetingTest
{
  // 5x1x5 open board. Player "Hero" (armed) at (0,0,0), enemy "Goon" at (3,0,0).
  private static UnitAction Row<TDefinition>(IReadOnlyList<UnitAction> actions)
    where TDefinition : UnitActionDefinition =>
    actions.AsValueEnumerable().Single(action => action.Action is TDefinition);

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
  public void MoveTargetingFlow()
  {
    using var battle = MakeArmedBattle();
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    var move = new MoveTargeting(battle.Runtime, hero.State);

    IReadOnlyCollection<Vector3I> reachable = move.Begin();
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
  public void AttackTargetingFlow()
  {
    using var battle = MakeArmedBattle();
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    Weapon rifle = hero.State.EquippedWeapon.RequireSome();
    var attack = new AttackTargeting(battle.Runtime, hero.State, rifle);

    IReadOnlyCollection<Vector3I> candidates = attack.Begin();
    Assert.True(candidates.AsValueEnumerable().Contains(new Vector3I(3, 0, 0)));   // the enemy's tile, in range + visible

    ActionPreview preview = attack.Preview(new Vector3I(3, 0, 0)).RequireRight();
    Assert.True(preview is AttackPreview);

    Assert.True(attack.CanCommit(new Vector3I(3, 0, 0)));
    Assert.False(attack.CanCommit(new Vector3I(1, 0, 0)));     // empty tile is not a candidate

    battle.Runtime.ExecuteAction(attack.Build(new Vector3I(3, 0, 0)));
  }

  [TestCase(TestName = "Boxed-in unit: MoveTargeting yields empty candidates and no committable tiles")]
  public void BoxedInMoveTargetingHasNoCommittableTiles()
  {
    // Default duel positions: player at (4,0,1), enemy at (4,0,4). Wall off every horizontal
    // neighbor of the player; the board is one tile tall, so there is no vertical escape.
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    Vector3I[] walls = [new(4, 0, 0), new(4, 0, 2), new(3, 0, 1), new(5, 0, 1)];
    foreach (Vector3I wall in walls)
      board.SetTileWalkable(board.ValidatePoint(wall).RequireSome(), false);
    using var battle = BattleFixture.Duel(board: board);
    var move = new MoveTargeting(battle.Runtime, battle.PlayerUnit);

    Assert.Equal(0, move.Begin().Count);
    Assert.False(move.CanCommit(new Vector3I(4, 0, 4))); // the enemy's tile
    Assert.False(move.CanCommit(new Vector3I(0, 0, 0))); // an open tile
  }

  [TestCase(TestName = "Out-of-range enemy: Attack row stays available with no committable tiles")]
  public void OutOfRangeEnemyKeepsAttackAvailableWithNoTargets()
  {
    // Knife (range 1) against an enemy across the board: the Attack verb stays usable, but
    // the candidate set is empty and even the enemy's own tile is not committable.
    using var battle = BattleFixture.Duel(
      player: new("Hero", Position: new Vector3I(0, 0, 0), Weapon: TestData.MakeWeapon("Knife", range: 1)),
      enemy: new("Goon", Position: new Vector3I(7, 0, 7)));

    var actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    Assert.True(Row<AttackActionDefinition>(actions).IsAvailable);
    var targeting = new AttackTargeting(battle.Runtime, battle.PlayerUnit,
      battle.PlayerUnit.EquippedWeapon.RequireSome());
    Assert.Equal(0, targeting.Begin().Count);
    Assert.False(targeting.CanCommit(new Vector3I(7, 0, 7)));
  }
}
