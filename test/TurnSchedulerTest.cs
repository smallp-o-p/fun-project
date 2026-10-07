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
    => battle.Read(() => new TurnScheduler(factionOrder,
      side => battle.Read(() => battle.Session.HasConsciousUnits(side)),
      side => battle.Read(() => battle.Session.GetFactionConsciousUnits(side))));

  [TestCase(TestName = "Construction activates the first conscious side and establishes its availability")]
  public void ConstructionActivatesTheFirstConsciousSide()
  {
    using var battle = BattleFixture.Duel();
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    battle.Read(() => Assert.Equal(battle.PlayerFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.Equal(2, scheduler.TurnQueue.Count));
    battle.Read(() => Assert.Equal(battle.PlayerFaction, scheduler.TurnQueue.AsValueEnumerable().First()));
    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.TurnQueue.AsValueEnumerable().Last()));
    battle.Read(() => Assert.True(scheduler.IsUnitAvailable(battle.PlayerUnit)));
    battle.Read(() => Assert.False(scheduler.IsUnitAvailable(battle.EnemyUnit)));
  }

  [TestCase(TestName = "A first declared faction without any unit is skipped")]
  public void ConstructionSkipsAnEmptyFirstFaction()
  {
    using var battle = BattleFixture.Duel();
    var stranger = TestData.MakeFaction("Stranger");
    var scheduler = SchedulerFor(battle, stranger, battle.PlayerFaction, battle.EnemyFaction);

    battle.Read(() => Assert.Equal(battle.PlayerFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.Equal(2, scheduler.TurnQueue.Count));
    battle.Read(() => Assert.False(scheduler.TurnQueue.AsValueEnumerable().Contains(stranger)));
  }

  [TestCase(TestName = "A first declared faction with only unconscious units is skipped")]
  public void ConstructionSkipsAKnockedOutFirstFaction()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.Equal(1, scheduler.TurnQueue.Count));
    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.TurnQueue.AsValueEnumerable().Single()));
  }

  [TestCase(TestName = "A first declared faction with only dead units is skipped")]
  public void ConstructionSkipsADeadFirstFaction()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 999);
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.Equal(1, scheduler.TurnQueue.Count));
    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.TurnQueue.AsValueEnumerable().Single()));
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

    battle.Read(() => Assert.Equal(2, scheduler.GlobalFactionTurnOrder.Count));
    battle.Read(() => Assert.Equal(battle.PlayerFaction, scheduler.GlobalFactionTurnOrder.AsValueEnumerable().First()));
    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.GlobalFactionTurnOrder.AsValueEnumerable().Last()));
    battle.Read(() => Assert.True(scheduler.IsUnitAvailable(battle.PlayerUnit)));
  }

  [TestCase(TestName = "Advance returns a complete turn or exhaustion")]
  public void AdvanceReturnsACompleteTurnOrExhaustion()
  {
    using var battle = BattleFixture.Duel();
    var scheduler = SchedulerFor(battle, battle.PlayerFaction, battle.EnemyFaction);

    var nextSide = scheduler.Advance();

    battle.Read(() => Assert.False(nextSide.Exhausted));
    battle.Read(() => Assert.False(nextSide.NextRound));
    battle.Read(() => Assert.Equal(battle.EnemyFaction, nextSide.NextSide));
    battle.Read(() => Assert.Equal(battle.EnemyFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.False(scheduler.IsUnitAvailable(battle.PlayerUnit)));
    battle.Read(() => Assert.True(scheduler.IsUnitAvailable(battle.EnemyUnit)));

    var rollover = scheduler.Advance();

    battle.Read(() => Assert.False(rollover.Exhausted));
    battle.Read(() => Assert.True(rollover.NextRound));
    battle.Read(() => Assert.Equal(battle.PlayerFaction, rollover.NextSide));
    battle.Read(() => Assert.Equal(battle.PlayerFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.True(scheduler.IsUnitAvailable(battle.PlayerUnit)));
    battle.Read(() => Assert.False(scheduler.IsUnitAvailable(battle.EnemyUnit)));

    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    var exhausted = scheduler.Advance();

    battle.Read(() => Assert.True(exhausted.Exhausted));
    battle.Read(() => Assert.Equal(battle.PlayerFaction, scheduler.ActiveSide));
    battle.Read(() => Assert.Equal(0, scheduler.TurnQueue.Count));
    battle.Read(() => Assert.False(scheduler.IsUnitAvailable(battle.PlayerUnit)));
    battle.Read(() => Assert.False(scheduler.IsUnitAvailable(battle.EnemyUnit)));
  }
}
