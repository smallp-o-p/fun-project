using FunProject.Battle;
using FunProject.Buffs;
using GdUnit4;
using Godot;

public partial class AlwaysMetBuffCondition : FunProject.Buffs.BuffCondition
{
  internal override bool IsMet(BattleSession session, BattleUnitState unit) => true;
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

    battle.Read(() => Assert.False(condition.IsMet(battle.Session, battle.Unit)));
    battle.ApplyDamage(battle.Unit, 10);
    battle.Read(() => Assert.False(condition.IsMet(battle.Session, battle.Unit)));
    battle.ApplyDamage(battle.Unit, 1);
    battle.Read(() => Assert.True(condition.IsMet(battle.Session, battle.Unit)));
    battle.Read(() => Assert.True(condition.IsMet(battle.Session, battle.Unit)));
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

    adjacent.Read(() => Assert.True(condition.IsMet(adjacent.Session, adjacent.PlayerUnit)));
    apart.Read(() => Assert.False(condition.IsMet(apart.Session, apart.PlayerUnit)));
  }

  [TestCase(TestName = "A condition subclass activates a buff without factory registration")]
  public void ConditionSubclassNeedsNoRegistration()
  {
    var faction = TestData.MakeFaction("Player");
    var buff = TestData.MakeBuff("Custom", new AlwaysMetBuffCondition());
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(8, 1, 8)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", faction, buffs: [buff])), new Vector3I(4, 0, 4))])],
      Seed: 7));
    var unit = battle.Unit;

    battle.Read(() => Assert.Equal(1, unit.ActiveBuffs.AsValueEnumerable().Count()));
  }
}
