using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class BattleEventTest
{
  [TestCase(TestName = "Committed battle events expose session domain objects")]
  public void CommittedBattleEventsExposeSessionDomainObjects()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var runtime = battle.Runtime;

    var start = battle.At(1, 0, 1);
    var destination = battle.At(1, 0, 2);
    var target = battle.At(3, 0, 2);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction, actionPoints: 6), start.Raw);
    var grenade = TestData.MakeGrenade("Frag Grenade", throwRange: 4, actionPointCost: 1);
    unit.AddInventoryItem(grenade.Item);
    battle.Start();

    runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [destination]));
    runtime.ExecuteAction(BattleAction.ThrowItem(battle.Alive(unit), grenade, target));
    runtime.ExecuteAction(BattleAction.ApplyDamage(battle.Alive(unit), 3));

    // The fixture window deliberately includes the spawn: UnitAdded is part of what this
    // test observes, so it is never cleared.
    var addedEvent = battle.Events.SingleEvent<UnitAddedBattleEvent>();
    Assert.True(ReferenceEquals(unit, addedEvent.Unit));
    Assert.Equal(start, addedEvent.Position);

    var movedEvent = battle.Events.SingleEvent<UnitMovedBattleEvent>();
    Assert.True(ReferenceEquals(unit, movedEvent.Unit));
    Assert.Equal(destination, movedEvent.Position);
    Assert.Equal(start, movedEvent.SourcePosition);

    var thrownEvent = battle.Events.SingleEvent<ItemThrownBattleEvent>();
    Assert.True(ReferenceEquals(unit, thrownEvent.Unit));
    Assert.True(ReferenceEquals(grenade.Item, thrownEvent.Item));
    Assert.Equal(target, thrownEvent.Position);

    var damagedEvent = battle.Events.SingleEvent<UnitDamagedBattleEvent>();
    Assert.True(ReferenceEquals(unit, damagedEvent.Unit));
  }
}
