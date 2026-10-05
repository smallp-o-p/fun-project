using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public class TurnSchedulerTest
{
  // An independent scheduler over a live fixture battle: real unit states feed the pure
  // roster callbacks, eligibility is arranged through ordinary damage before construction,
  // and the fixture's own scheduler/executor stay untouched.
  private static TurnScheduler SchedulerFor(BattleFixture battle, params Faction[] factionOrder)
    => new(factionOrder, battle.Session.HasConsciousUnits, battle.Session.GetFactionConsciousUnits);

  [TestCase(TestName = "Construction activates the first conscious side and establishes its availability")]
  public void ConstructionActivatesTheFirstConsciousSide()
  {
    using var battle = BattleFixture.Duel();
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    Assert.Equal(battle.PlayerFaction, scheduler.ActiveSide);
    Assert.Equal(2, scheduler.TurnQueue.Count);
    Assert.Equal(battle.PlayerFaction, scheduler.TurnQueue.AsValueEnumerable().First());
    Assert.Equal(battle.EnemyFaction, scheduler.TurnQueue.AsValueEnumerable().Last());
    Assert.True(scheduler.IsUnitAvailable(battle.PlayerUnit));
    Assert.False(scheduler.IsUnitAvailable(battle.EnemyUnit));
  }

  [TestCase(TestName = "A first declared faction without any unit is skipped")]
  public void ConstructionSkipsAnEmptyFirstFaction()
  {
    using var battle = BattleFixture.Duel();
    var stranger = TestData.MakeFaction("Stranger");
    var scheduler = SchedulerFor(battle, stranger, battle.PlayerFaction, battle.EnemyFaction);

    Assert.Equal(battle.PlayerFaction, scheduler.ActiveSide);
    Assert.Equal(2, scheduler.TurnQueue.Count);
    Assert.False(scheduler.TurnQueue.AsValueEnumerable().Contains(stranger));
  }

  [TestCase(TestName = "A first declared faction with only unconscious units is skipped")]
  public void ConstructionSkipsAKnockedOutFirstFaction()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    Assert.Equal(battle.EnemyFaction, scheduler.ActiveSide);
    Assert.Equal(1, scheduler.TurnQueue.Count);
    Assert.Equal(battle.EnemyFaction, scheduler.TurnQueue.AsValueEnumerable().Single());
  }

  [TestCase(TestName = "A first declared faction with only dead units is skipped")]
  public void ConstructionSkipsADeadFirstFaction()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 999);
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    Assert.Equal(battle.EnemyFaction, scheduler.ActiveSide);
    Assert.Equal(1, scheduler.TurnQueue.Count);
    Assert.Equal(battle.EnemyFaction, scheduler.TurnQueue.AsValueEnumerable().Single());
  }

  [TestCase(TestName = "Construction rejects a faction order without any conscious unit")]
  public void ConstructionRejectsAFactionOrderWithoutConsciousUnits()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Throws<InvalidOperationException>(
      () => SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction));
  }

  [TestCase(TestName = "Duplicate faction identities keep the first occurrence only")]
  public void DuplicateFactionIdentitiesRetainFirstOccurrenceOnly()
  {
    using var battle = BattleFixture.Duel();
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction, battle.PlayerFaction);

    Assert.Equal(2, scheduler.GlobalFactionTurnOrder.Count);
    Assert.Equal(battle.PlayerFaction, scheduler.GlobalFactionTurnOrder.AsValueEnumerable().First());
    Assert.Equal(battle.EnemyFaction, scheduler.GlobalFactionTurnOrder.AsValueEnumerable().Last());
    Assert.True(scheduler.IsUnitAvailable(battle.PlayerUnit));
  }
}
