using FunProject.Battle;
using FunProject.Stats;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class ObjectHealthTest
{
  [TestCase]
  public void HealthInstancesAreIndependentAndAuthoredHealthIsUnchanged()
  {
    var stat = new HealthStat { BaseValue = 10 };
    var data = new ObjectHealthCapabilityData { HealthStat = stat };
    var first = (ObjectHealthCapability)data.CreateRuntime();
    var second = (ObjectHealthCapability)data.CreateRuntime();
    first.Reduce(3);
    Assert.Equal(10, first.MaxHealth);
    Assert.Equal(7, first.CurrentHealth);
    Assert.Equal(10, second.CurrentHealth);
    Assert.Equal(10, stat.BaseValue);
    first.Reduce(999);
    Assert.Equal(0, first.CurrentHealth);
    Assert.Throws<ArgumentOutOfRangeException>(() => first.Reduce(-1));
  }

  [TestCase]
  public void MissingNonpositiveAndDuplicateHealthAreRejected()
  {
    Assert.Throws<InvalidOperationException>(() =>
      new ObjectHealthCapabilityData { HealthStat = null! }.CreateRuntime());
    foreach (int amount in new[] { 0, -1 })
      Assert.Throws<ArgumentOutOfRangeException>(() =>
        new ObjectHealthCapabilityData
        {
          HealthStat = new HealthStat { BaseValue = amount },
        }.CreateRuntime());

    using var battle = BattleFixture.Duel(start: false);
    var duplicate = TestData.MakeObject("Broken", 10,
      new ObjectHealthCapabilityData { HealthStat = new HealthStat { BaseValue = 20 } });
    Assert.Throws<InvalidOperationException>(() =>
      battle.PlaceObject(duplicate, new Vector3I(2, 0, 2)));
  }

  [TestCase]
  public void MintRequiresHealthMembershipAndLivePlacement()
  {
    using var battle = BattleFixture.Duel(start: false);
    using var foreign = BattleFixture.Duel(start: false);
    var crate = battle.PlaceObject(TestData.MakeObject("Crate", 10,
      new InteractiveCapabilityData()), new Vector3I(3, 0, 1));
    var scenery = battle.PlaceObject(TestData.MakeObject(), new Vector3I(2, 0, 2));
    var identity = new BattleEntity.Object(crate);
    Assert.True(ReferenceEquals(identity, battle.Runtime.TryGetAttackTarget(identity).RequireSome().Entity));
    Assert.True(battle.Runtime.TryGetAttackTarget(new BattleEntity.Object(scenery)).IsNone);
    Assert.True(foreign.Runtime.TryGetAttackTarget(identity).IsNone);
    Assert.True(foreign.Runtime.TryGetAttackTarget(new BattleEntity.Unit(battle.PlayerUnit)).IsNone);
    Assert.Equal(battle.Alive(battle.EnemyUnit).Position, battle.Target(battle.EnemyUnit).Position);
    battle.Start();
    battle.Interact(battle.PlayerUnit, crate);
    Assert.True(battle.Runtime.TryGetAttackTarget(identity).IsNone);
  }
}
