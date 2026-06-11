using FunProject.Battle;
using FunProject.Core;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class BattleEventTest
{
  [TestCase(TestName = "Battle events build display strings from event data")]
  public void BattleEventsBuildDisplayStringsFromEventData()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var runtime = new BattleRuntime(session);
    var sourcePosition = session.Board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome();
    var position = session.Board.ValidatePoint(new Vector3I(1, 0, 2)).RequireSome();
    var unit = BattleActionTestHelper.SpawnUnit(
      runtime,
      BattleTestFactory.MakeCombatant("Alpha", faction),
      sourcePosition.Raw);
    var grenade = BattleTestFactory.MakeGrenade("Frag Grenade");

    Assert.Equal("Battle started.", new SessionStartedBattleEvent().ToDisplayString());
    Assert.Equal("Battle ended.", new SessionEndedBattleEvent().ToDisplayString());
    Assert.Equal("Turn 2 started for Player.", new TurnStartedBattleEvent(faction, 2).ToDisplayString());
    Assert.Equal("Turn 2 ended for Player.", new TurnEndedBattleEvent(faction, 2).ToDisplayString());
    Assert.Equal("Active side is now Player.", new ActiveSideChangedBattleEvent(faction).ToDisplayString());
    Assert.Equal($"Alpha entered the battle at {position}.", new UnitAddedBattleEvent(unit.State, position).ToDisplayString());
    Assert.Equal("Alpha ended their activation.", new UnitActivationEndedBattleEvent(unit.State, position).ToDisplayString());
    Assert.Equal($"Unit ID {unit.UnitId} moved from {sourcePosition} to {position}.", new UnitMovedBattleEvent(unit.State, position, sourcePosition).ToDisplayString());
    Assert.Equal($"Unit ID {unit.UnitId} occupied {position}.", new TileOccupiedBattleEvent(unit.State, position).ToDisplayString());
    Assert.Equal("Damage: 3 (0 armor, 3 health)", new UnitDamagedBattleEvent(unit.State, [new Damage(3, Element.Kinetic)], 0, 3).ToDisplayString());
    Assert.Equal($"Unit ID {unit.UnitId} was killed!", new UnitKilledBattleEvent(unit.State, position).ToDisplayString());
    Assert.Equal("Alpha threw Frag Grenade.", new ItemThrownBattleEvent(unit.State, position, grenade.Item).ToDisplayString());
  }

  [TestCase(TestName = "Battle events expose stable event names")]
  public void BattleEventsExposeStableEventNames()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var runtime = new BattleRuntime(session);
    var sourcePosition = session.Board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome();
    var position = session.Board.ValidatePoint(new Vector3I(1, 0, 2)).RequireSome();
    var unit = BattleActionTestHelper.SpawnUnit(
      runtime,
      BattleTestFactory.MakeCombatant("Alpha", faction),
      sourcePosition.Raw);
    var grenade = BattleTestFactory.MakeGrenade("Frag Grenade");

    (BattleEvent Event, string ExpectedName)[] events =
    [
      (new SessionStartedBattleEvent(), "session_started"),
      (new SessionEndedBattleEvent(), "session_ended"),
      (new TurnStartedBattleEvent(faction, 2), "turn_started"),
      (new TurnEndedBattleEvent(faction, 2), "turn_ended"),
      (new ActiveSideChangedBattleEvent(faction), "active_side_changed"),
      (new UnitAddedBattleEvent(unit.State, position), "unit_added"),
      (new UnitActivationEndedBattleEvent(unit.State, position), "unit_activation_ended"),
      (new UnitMovedBattleEvent(unit.State, position, sourcePosition), "unit_moved"),
      (new TileOccupiedBattleEvent(unit.State, position), "tile_occupied"),
      (new UnitDamagedBattleEvent(unit.State, [new Damage(3, Element.Kinetic)], 0, 3), "unit_damaged"),
      (new UnitKilledBattleEvent(unit.State, position), "unit_killed"),
      (new ItemThrownBattleEvent(unit.State, position, grenade.Item), "item_thrown"),
    ];

    foreach ((BattleEvent battleEvent, string expectedName) in events)
      Assert.Equal(expectedName, battleEvent.EventName);
  }

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
