using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using System.Collections.Generic;

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
  public void MoveTargetingFlow()
  {
    using var battle = MakeArmedBattle();
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    var move = new MoveTargeting(battle.Runtime, hero.State);

    IReadOnlyCollection<Vector3I> reachable = move.Begin();
    Assert.True(reachable.AsValueEnumerable().Contains(new Vector3I(2, 0, 1)));

    ActionPreview preview = GetValue(move.Preview(new Vector3I(2, 0, 1)));
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

    ActionPreview preview = GetValue(attack.Preview(new Vector3I(3, 0, 0)));
    Assert.True(preview is AttackPreview);

    Assert.True(attack.CanCommit(new Vector3I(3, 0, 0)));
    Assert.False(attack.CanCommit(new Vector3I(1, 0, 0)));     // empty tile is not a candidate

    battle.Runtime.ExecuteAction(attack.Build(new Vector3I(3, 0, 0)));
  }
}
