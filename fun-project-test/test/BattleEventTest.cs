using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class BattleEventTest
{
  [TestCase(TestName = "Committed battle events expose session domain objects")]
  public void CommittedBattleEventsExposeSessionDomainObjects()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [faction]);
    var runtime = new BattleRuntime(session);
    var raisedEvents = new List<BattleEvent>();
    runtime.BattleEventCommitted += raisedEvents.Add;

    var start = session.Board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome();
    var destination = session.Board.ValidatePoint(new Vector3I(1, 0, 2)).RequireSome();
    var target = session.Board.ValidatePoint(new Vector3I(3, 0, 2)).RequireSome();
    var unit = BattleActionTestHelper.SpawnUnit(
      runtime,
      BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 6),
      start.Raw);
    var grenade = BattleTestFactory.MakeGrenade("Frag Grenade", throwRange: 4, actionPointCost: 1);
    unit.State.AddInventoryItem(grenade.Item);
    BattleActionTestHelper.EnsureEveryFactionHasObjective(session);
    BattleActionTestHelper.StartBattle(runtime);

    runtime.ExecuteAction(BattleAction.MoveUnit(unit.State, [destination.Raw])).RequireSingleResult();
    runtime.ExecuteAction(BattleAction.ThrowItem(unit.State, grenade, target.Raw)).RequireSingleResult();
    runtime.ExecuteAction(BattleAction.ApplyDamage(unit.State, 3)).RequireSingleResult();

    var addedEvent = raisedEvents.OfType<UnitAddedBattleEvent>().Single();
    Assert.True(ReferenceEquals(unit.State, addedEvent.Unit));
    Assert.Equal(start, addedEvent.Position);

    var movedEvent = raisedEvents.OfType<UnitMovedBattleEvent>().Single();
    Assert.True(ReferenceEquals(unit.State, movedEvent.Unit));
    Assert.Equal(destination, movedEvent.Position);
    Assert.Equal(start, movedEvent.SourcePosition);

    var thrownEvent = raisedEvents.OfType<ItemThrownBattleEvent>().Single();
    Assert.True(ReferenceEquals(unit.State, thrownEvent.Unit));
    Assert.True(ReferenceEquals(grenade.Item, thrownEvent.Item));
    Assert.Equal(target, thrownEvent.Position);

    var damagedEvent = raisedEvents.OfType<UnitDamagedBattleEvent>().Single();
    Assert.True(ReferenceEquals(unit.State, damagedEvent.Unit));
  }
}
