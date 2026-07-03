using FunProject.Battle;
using FunProject.Buffs;
using GdUnit4;
using Godot;
using System;

// A condition data type deliberately unknown to BuffCondition.Create.
public partial class UnmappedBuffConditionData : BuffConditionData
{
}

[TestSuite]
[RequireGodotRuntime]
public partial class BuffConditionTest
{
  [TestCase(TestName = "HealthBelowPercent is false at and above the threshold, true strictly below")]
  public void HealthBelowPercentThreshold()
  {
    var battle = StartSoloBattle(new Vector3I(8, 1, 8), new Vector3I(4, 0, 4), health: 20);
    var buff = new Buff(MakeBuff("Frenzy", new HealthBelowPercentConditionData { Percent = 50f }));

    Assert.False(buff.Evaluate(battle.Session, battle.Unit.State));
    Assert.False(buff.IsActive);

    ApplyDamage(battle.Session, battle.Unit.State, 10); // 10/20 = exactly 50%: not strictly below
    Assert.False(buff.Evaluate(battle.Session, battle.Unit.State));

    ApplyDamage(battle.Session, battle.Unit.State, 1); // 9/20: below
    Assert.True(buff.Evaluate(battle.Session, battle.Unit.State));
    Assert.True(buff.IsActive);

    // Condition still met: no flip on re-evaluation.
    Assert.False(buff.Evaluate(battle.Session, battle.Unit.State));
    Assert.True(buff.IsActive);
  }

  [TestCase(TestName = "AdjacentEnemy is true only when a living enemy is orthogonally adjacent")]
  public void AdjacentEnemyRequiresAdjacency()
  {
    var adjacent = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Position: new Vector3I(4, 0, 3)),
      Enemy = new DuelSide("Hostile", Position: new Vector3I(4, 0, 4)),
    }.Start();
    var buff = new Buff(MakeBuff("Riposte", new AdjacentEnemyConditionData()));
    Assert.True(buff.Evaluate(adjacent.Session, adjacent.PlayerUnit.State));

    var apart = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Position: new Vector3I(4, 0, 1)),
      Enemy = new DuelSide("Hostile", Position: new Vector3I(4, 0, 4)),
    }.Start();
    var farBuff = new Buff(MakeBuff("Riposte", new AdjacentEnemyConditionData()));
    Assert.False(farBuff.Evaluate(apart.Session, apart.PlayerUnit.State));
  }

  [TestCase(TestName = "Create throws on an unmapped condition data type")]
  public void CreateThrowsOnUnmappedData()
  {
    bool thrown = false;
    try
    {
      BuffCondition.Create(new UnmappedBuffConditionData());
    }
    catch (ArgumentException)
    {
      thrown = true;
    }

    Assert.True(thrown);
  }

  [TestCase(TestName = "Buff construction throws when the data has no condition")]
  public void BuffRequiresCondition()
  {
    bool thrown = false;
    try
    {
      // Condition is `required` (compile-time), but authored .tres files can still carry
      // null past Godot deserialization — the runtime guard is what this test covers.
      _ = new Buff(new BuffData { Name = "Broken", Condition = null });
    }
    catch (InvalidOperationException)
    {
      thrown = true;
    }

    Assert.True(thrown);
  }
}
