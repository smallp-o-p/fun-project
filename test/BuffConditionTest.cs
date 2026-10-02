using FunProject.Battle;
using FunProject.Buffs;
using GdUnit4;
using Godot;

public partial class AlwaysMetBuffCondition : FunProject.Buffs.BuffCondition
{
  internal override bool IsMet(BattleReadContext context, BattleUnitState unit) => true;
}

[TestSuite]
[RequireGodotRuntime]
public partial class BuffConditionTest
{
  [TestCase(TestName = "Health predicate is false at the threshold and true strictly below")]
  public void HealthBelowPercentThreshold()
  {
    using var battle = BattleFixture.Solo(new Vector3I(8, 1, 8), new Vector3I(4, 0, 4), health: 20);
    var condition = new FunProject.Buffs.HealthBelowPercentCondition { Percent = 50f };

    Assert.False(condition.IsMet(battle.Read, battle.Unit));
    battle.ApplyDamage(battle.Unit, 10);
    Assert.False(condition.IsMet(battle.Read, battle.Unit));
    battle.ApplyDamage(battle.Unit, 1);
    Assert.True(condition.IsMet(battle.Read, battle.Unit));
    Assert.True(condition.IsMet(battle.Read, battle.Unit));
  }

  [TestCase(TestName = "Adjacent-enemy predicate reads the supplied battle")]
  public void AdjacentEnemyRequiresAdjacency()
  {
    using var adjacent = BattleFixture.Duel(
      player: new("Alpha", Position: new Vector3I(4, 0, 3)),
      enemy: new("Hostile", Position: new Vector3I(4, 0, 4)));
    using var apart = BattleFixture.Duel(
      player: new("Alpha", Position: new Vector3I(4, 0, 1)),
      enemy: new("Hostile", Position: new Vector3I(4, 0, 4)));
    var condition = new FunProject.Buffs.AdjacentEnemyCondition();

    Assert.True(condition.IsMet(adjacent.Read, adjacent.PlayerUnit));
    Assert.False(condition.IsMet(apart.Read, apart.PlayerUnit));
  }

  [TestCase(TestName = "A condition subclass activates a buff without factory registration")]
  public void ConditionSubclassNeedsNoRegistration()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var buff = TestData.MakeBuff("Custom", new AlwaysMetBuffCondition());
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction, buffs: [buff]), new Vector3I(4, 0, 4));

    Assert.Equal(1, unit.ActiveBuffs.AsValueEnumerable().Count());
  }
}
