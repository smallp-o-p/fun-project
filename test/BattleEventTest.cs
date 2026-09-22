using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Frozen;

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

  [TestCase(TestName = "Event dispatch keys contain exactly the concrete type and its tag interfaces")]
  public void EventDispatchKeysContainExactlyConcreteTypeAndTagInterfaces()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    UnitAddedBattleEvent addedEvent = battle.Events.SingleEvent<UnitAddedBattleEvent>();
    FrozenSet<Type> keys = addedEvent.EventKeys;

    Assert.Equal(4, keys.Count);
    Assert.True(keys.Contains(typeof(UnitAddedBattleEvent)), "Concrete event type missing from dispatch keys.");
    Assert.True(keys.Contains(typeof(BattleEventTag)), "Root tag missing from dispatch keys.");
    Assert.True(keys.Contains(typeof(IUnitBattleEvent)), "IUnitBattleEvent missing from dispatch keys.");
    Assert.True(keys.Contains(typeof(IPositionedBattleEvent)), "IPositionedBattleEvent missing from dispatch keys.");
  }

  [TestCase(TestName = "An event without marker interfaces keys on its concrete type and the root tag only")]
  public void EventWithoutMarkerInterfacesKeysOnConcreteTypeAndRootTag()
  {
    FrozenSet<Type> keys = new SessionStartedBattleEvent().EventKeys;

    Assert.Equal(2, keys.Count);
    Assert.True(keys.Contains(typeof(SessionStartedBattleEvent)), "Concrete event type missing from dispatch keys.");
    Assert.True(keys.Contains(typeof(BattleEventTag)), "Root tag missing from dispatch keys.");
  }

  [TestCase(TestName = "Instances of one event type share one static keys set; different event types do not")]
  public void SameEventTypeSharesOneKeysSetWhileDifferentEventTypesDoNot()
  {
    FrozenSet<Type> firstStarted = new SessionStartedBattleEvent().EventKeys;
    FrozenSet<Type> secondStarted = new SessionStartedBattleEvent().EventKeys;
    FrozenSet<Type> ended = new SessionEndedBattleEvent().EventKeys;

    // The keys set is a per-closed-type static, so instances of one event type share the
    // set instance and different event types each own their own.
    Assert.True(ReferenceEquals(firstStarted, secondStarted));
    Assert.False(ReferenceEquals(firstStarted, ended));
  }

  // No compile-time enforcement exists for the sealed/self-keyed contract that keeps the
  // per-closed-type dispatch keys authoritative, so this test-only guard fails naming the
  // offender. GetTypes() scans the Debug test-inclusive assembly, so test-defined events
  // (e.g. BattleHookTest's probe) are covered by the same scan — intended.
  [TestCase(TestName = "Every concrete battle event is sealed and self-keyed")]
  public void EveryConcreteBattleEventIsSealedAndSelfKeyed()
  {
    Type[] concreteEvents = typeof(BattleEvent).Assembly.GetTypes()
      .AsValueEnumerable()
      .Where(type => !type.IsAbstract && typeof(BattleEvent).IsAssignableFrom(type))
      .ToArray();

    Assert.True(concreteEvents.Length > 0, "No concrete battle events found; the scan is vacuous.");

    foreach (Type eventType in concreteEvents)
    {
      Assert.True(eventType.IsSealed,
        $"{eventType.FullName} must be sealed so its runtime type always equals the BattleEvent<TSelf> argument owning its dispatch keys.");
      Assert.True(HasSelfKeyedBaseChain(eventType),
        $"{eventType.FullName} must derive from BattleEvent<{eventType.Name}> so its dispatch keys are owned by itself.");
    }
  }

  private static bool HasSelfKeyedBaseChain(Type eventType)
  {
    for (Type? baseType = eventType.BaseType; baseType is not null; baseType = baseType.BaseType)
      if (baseType.IsConstructedGenericType
          && baseType.GetGenericTypeDefinition() == typeof(BattleEvent<>)
          && baseType.GenericTypeArguments[0] == eventType)
        return true;
    return false;
  }

  [TestCase]
  public void ObjectOutcomeEventsHaveTypedIdentityAndGuardTheirPayloads()
  {
    using var battle = BattleFixture.Duel(start: false,
      player: new("Shooter", Aim: 100, Weapon: TestData.MakeWeapon("Rifle")));
    var obj = battle.PlaceObject(TestData.MakeObject("Crate", 10), new Vector3I(3, 0, 2));
    Assert.Throws<ArgumentNullException>(() => new ObjectDamagedBattleEvent(null!, [], 1, None));
    Assert.Throws<ArgumentNullException>(() => new ObjectDamagedBattleEvent(obj, null!, 1, None));
    foreach (int amount in new[] { 0, -1 })
      Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectDamagedBattleEvent(obj, [], amount, None));
    Assert.Throws<ArgumentNullException>(() => new ObjectDestroyedBattleEvent(null!, battle.At(3, 0, 2), None));
    BattleEvent damaged = new ObjectDamagedBattleEvent(obj, [new Damage(1, Element.Kinetic)], 1, None);
    BattleEvent destroyed = new ObjectDestroyedBattleEvent(obj, battle.At(3, 0, 2), None);
    Assert.True(damaged is ICausedByUnit);
    Assert.True(destroyed is IPositionedBattleEvent and ICausedByUnit);
    Assert.False(damaged is IUnitBattleEvent);
    Assert.False(destroyed is IUnitBattleEvent);
    battle.Start();
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    Assert.True(ReferenceEquals(battle.EnemyUnit,
      ((BattleEntity.Unit)battle.Events.SingleEvent<UnitAttackedBattleEvent>().Target).State));
  }
}
