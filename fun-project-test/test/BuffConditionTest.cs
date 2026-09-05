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
    var battle = StartSoloBattle(new Vector3I(8, 1, 8), new Vector3I(4, 0, 4), health: 20);
    var condition = new FunProject.Buffs.HealthBelowPercentCondition { Percent = 50f };

    Assert.False(condition.IsMet(battle.Session, battle.Unit.State));
    ApplyDamage(battle.Session, battle.Unit.State, 10);
    Assert.False(condition.IsMet(battle.Session, battle.Unit.State));
    ApplyDamage(battle.Session, battle.Unit.State, 1);
    Assert.True(condition.IsMet(battle.Session, battle.Unit.State));
    Assert.True(condition.IsMet(battle.Session, battle.Unit.State));
  }

  [TestCase(TestName = "Adjacent-enemy predicate reads the supplied battle")]
  public void AdjacentEnemyRequiresAdjacency()
  {
    var adjacent = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Position: new Vector3I(4, 0, 3)),
      Enemy = new DuelSide("Hostile", Position: new Vector3I(4, 0, 4)),
    }.Start();
    var apart = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Position: new Vector3I(4, 0, 1)),
      Enemy = new DuelSide("Hostile", Position: new Vector3I(4, 0, 4)),
    }.Start();
    var condition = new FunProject.Buffs.AdjacentEnemyCondition();

    Assert.True(condition.IsMet(adjacent.Session, adjacent.PlayerUnit.State));
    Assert.False(condition.IsMet(apart.Session, apart.PlayerUnit.State));
  }

  [TestCase(TestName = "A condition subclass activates a buff without factory registration")]
  public void ConditionSubclassNeedsNoRegistration()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var buff = MakeBuff("Custom", new AlwaysMetBuffCondition());
    var unit = SpawnUnit(session, MakeCombatant("Alpha", faction, buffs: [buff]), new Vector3I(4, 0, 4));

    Assert.Equal(1, unit.State.ActiveBuffs.AsValueEnumerable().Count());
  }
}
