using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class ActionTargetingTest
{
  private static T QueryRight<T>(Either<BattleQueryFailure, T> result) =>
    result.Match(Right: v => v, Left: f => throw new Exception($"Query failed: {f.Message}"));

  // 5x1x5 open board. Player "Hero" (armed) at (0,0,0), enemy "Goon" at (3,0,0).
  private static (BattleRuntime Runtime, BattleUnitState Hero, BattleUnitState Goon, Weapon Weapon) MakeArmedBattle()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    var rifle = BattleTestFactory.MakeWeapon("Rifle");
    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Hero", player), rifle), new Vector3I(0, 0, 0)),
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 0)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var runtime = BattleFactory.Start(new BattleSetup(board, new[] { player, enemy }, placements, objectives))
      .Match(Right: r => r, Left: f => throw new Exception($"Setup failed: {f.Message}"));
    var hero = QueryRight(runtime.Query(new GetFactionAliveUnits(player))).Single();
    var goon = QueryRight(runtime.Query(new GetFactionAliveUnits(enemy))).Single();
    return (runtime, hero, goon, rifle);
  }

  [TestCase(TestName = "MoveTargeting: Begin reachable, Preview path, Build moves the unit")]
  public void MoveTargetingFlow()
  {
    var (runtime, hero, _, _) = MakeArmedBattle();
    var move = new MoveTargeting(runtime, hero);

    IReadOnlyCollection<Vector3I> reachable = move.Begin();
    Assert.True(reachable.Contains(new Vector3I(2, 0, 1)));

    ActionPreview preview = QueryRight(move.Preview(new Vector3I(2, 0, 1)));
    Assert.True(preview is PathPreview);
    PathPreview pathPreview = (PathPreview)preview;
    Assert.Equal(new Vector3I(0, 0, 0), pathPreview.Path[0]);
    Assert.Equal(new Vector3I(2, 0, 1), pathPreview.Path[^1]);

    Assert.True(move.CanCommit(new Vector3I(2, 0, 1)));
    runtime.ExecuteAction(move.Build(new Vector3I(2, 0, 1)));
    Assert.Equal(new Vector3I(2, 0, 1), QueryRight(runtime.Query(new GetUnitPosition(hero))).Raw);
  }

  [TestCase(TestName = "AttackTargeting: Begin candidate enemy tile, Preview hit chance, Build attacks")]
  public void AttackTargetingFlow()
  {
    var (runtime, hero, goon, rifle) = MakeArmedBattle();
    var attack = new AttackTargeting(runtime, hero, rifle);

    IReadOnlyCollection<Vector3I> candidates = attack.Begin();
    Assert.True(candidates.Contains(new Vector3I(3, 0, 0)));   // the enemy's tile, in range + visible

    ActionPreview preview = QueryRight(attack.Preview(new Vector3I(3, 0, 0)));
    Assert.True(preview is AttackPreview);

    Assert.True(attack.CanCommit(new Vector3I(3, 0, 0)));
    Assert.False(attack.CanCommit(new Vector3I(1, 0, 0)));     // empty tile is not a candidate

    var results = runtime.ExecuteAction(attack.Build(new Vector3I(3, 0, 0)));
    Assert.True(results.Single().Succeeded);
  }
}
