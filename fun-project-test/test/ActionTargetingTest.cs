using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class ActionTargetingTest
{
  // 5x1x5 open board. Player "Hero" (armed) at (0,0,0), enemy "Goon" at (3,0,0).
  private static (BattleRuntime Runtime, BattleUnitState Hero, BattleUnitState Goon, Weapon Weapon) MakeArmedBattle()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var rifle = BattleTestFactory.MakeWeapon("Rifle");
    var runtime = StartRuntime(
      new Vector3I(5, 1, 5),
      new StartPlacement(player, BattleTestFactory.MakeCombatant("Hero", player), new Vector3I(0, 0, 0), rifle),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("Goon", enemy), new Vector3I(3, 0, 0)));
    var hero = SingleAliveUnit(runtime, player);
    var goon = SingleAliveUnit(runtime, enemy);
    return (runtime, hero, goon, rifle);
  }

  [TestCase(TestName = "MoveTargeting: Begin reachable, Preview path, Build moves the unit")]
  public void MoveTargetingFlow()
  {
    var (runtime, hero, _, _) = MakeArmedBattle();
    var move = new MoveTargeting(runtime, hero);

    IReadOnlyCollection<Vector3I> reachable = move.Begin();
    Assert.True(reachable.Contains(new Vector3I(2, 0, 1)));

    ActionPreview preview = GetValue(move.Preview(new Vector3I(2, 0, 1)));
    Assert.True(preview is PathPreview);
    PathPreview pathPreview = (PathPreview)preview;
    Assert.Equal(new Vector3I(0, 0, 0), pathPreview.Path[0]);
    Assert.Equal(new Vector3I(2, 0, 1), pathPreview.Path[^1]);

    Assert.True(move.CanCommit(new Vector3I(2, 0, 1)));
    runtime.ExecuteAction(move.Build(new Vector3I(2, 0, 1)));
    Assert.Equal(new Vector3I(2, 0, 1), GetValue(runtime.Query(new GetUnitPosition(hero))).Raw);
  }

  [TestCase(TestName = "AttackTargeting: Begin candidate enemy tile, Preview hit chance, Build attacks")]
  public void AttackTargetingFlow()
  {
    var (runtime, hero, goon, rifle) = MakeArmedBattle();
    var attack = new AttackTargeting(runtime, hero, rifle);

    IReadOnlyCollection<Vector3I> candidates = attack.Begin();
    Assert.True(candidates.Contains(new Vector3I(3, 0, 0)));   // the enemy's tile, in range + visible

    ActionPreview preview = GetValue(attack.Preview(new Vector3I(3, 0, 0)));
    Assert.True(preview is AttackPreview);

    Assert.True(attack.CanCommit(new Vector3I(3, 0, 0)));
    Assert.False(attack.CanCommit(new Vector3I(1, 0, 0)));     // empty tile is not a candidate

    var results = runtime.ExecuteAction(attack.Build(new Vector3I(3, 0, 0)));
    Assert.True(results.Single().Succeeded);
  }
}
