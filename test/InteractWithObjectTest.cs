using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class InteractWithObjectTest
{
  private static BattleSpecialObjectData MakeInteractiveObjectData(int actionPointCost = 2)
  {
    var data = new BattleSpecialObjectData { Name = "Bomb" };
    data.Capabilities.Add(new InteractiveCapabilityData { ActionPointCost = actionPointCost });
    return data;
  }

  [TestCase(TestName = "Adjacent interact spends AP, defuses, clears occupancy, raises event")]
  public void HappyPathInteract()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    var unit = battle.Spawn(TestData.MakeCombatant("A", player), new Vector3I(1, 0, 1));
    battle.Spawn(TestData.MakeCombatant("B", enemy), new Vector3I(3, 0, 3));
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 1));
    var bomb = battle.PlaceObject(MakeInteractiveObjectData(), point.Raw);
    battle.Start();
    battle.ClearEvents();

    int apBefore = unit.CurrentActionPoints;
    battle.Interact(unit, bomb);

    Assert.Equal(Some(ObjectStatus.Interacted), bomb.Status);
    Assert.Equal(apBefore - 2, unit.CurrentActionPoints);
    Assert.True(battle.Session.TryGetAliveObject(bomb).IsNone);
    Assert.False(battle.Board.IsBlockedByObject(point));
    Assert.Equal(point.Raw, bomb.Position);

    ObjectInteractedBattleEvent @event = battle.Events.SingleEvent<ObjectInteractedBattleEvent>();
    Assert.Equal(unit, @event.Actor);
    Assert.Equal(bomb, @event.Object);
    Assert.Equal(point, @event.Position);
  }

  [TestCase(TestName = "Interaction spending the actor's last AP refreshes retained actions only for that actor")]
  public void InteractionSpendingLastActionPointRefreshesActorOptions()
  {
    var weapon = TestData.MakeWeapon("Rifle");
    using var battle = BattleFixture.Duel(
      player: new("Alpha", ActionPoints: 2, Weapon: weapon),
      enemy: new("Hostile"),
      start: false);
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(5, 0, 1));
    BattleObjectState bomb = battle.PlaceObject(MakeInteractiveObjectData(actionPointCost: 2), point.Raw);
    battle.Start();

    IReadOnlyList<UnitAction> playerActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    IReadOnlyList<UnitAction> enemyActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.EnemyUnit)));
    foreach (UnitAction action in playerActions)
      _ = action.IsAvailable;
    foreach (UnitAction action in enemyActions)
      _ = action.IsAvailable;
    int roundNumber = battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber;

    battle.Interact(battle.PlayerUnit, bomb);

    Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(roundNumber, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.False(playerActions.AsValueEnumerable()
      .Single(action => action.Action is MoveActionDefinition).IsAvailable);
    Assert.False(playerActions.AsValueEnumerable()
      .Single(action => action.Action is AttackActionDefinition).IsAvailable);
    Assert.False(playerActions.AsValueEnumerable()
      .Single(action => action.Action is PassActionDefinition).IsAvailable);
    Assert.True(playerActions.AsValueEnumerable()
      .Single(action => action.Action is EndTurnActionDefinition).IsAvailable);
    Assert.True(enemyActions.AsValueEnumerable().All(action => !action.IsDirty));
    Assert.True(ReferenceEquals(playerActions, battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)))));
    Assert.True(ReferenceEquals(enemyActions, battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.EnemyUnit)))));
  }

  [TestCase(TestName = "Insufficient action points throw before object mutation")]
  public void InsufficientActionPointsDoNotMutateObject()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    var unit = battle.Spawn(TestData.MakeCombatant("A", player, actionPoints: 1), new Vector3I(1, 0, 1));
    battle.Spawn(TestData.MakeCombatant("B", enemy), new Vector3I(3, 0, 3));
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 1));
    var bomb = battle.PlaceObject(MakeInteractiveObjectData(actionPointCost: 2), point.Raw);
    battle.Start();
    battle.ClearEvents();

    Assert.Throws<InvalidOperationException>(() => battle.Submit(
      BattleAction.InteractWithObject(battle.Alive(unit), battle.Live(bomb))));

    Assert.True(bomb.Status.IsNone);
    Assert.True(battle.Session.TryGetAliveObject(bomb).IsSome);
    Assert.True(battle.Board.IsBlockedByObject(point));
    Assert.Equal(point.Raw, bomb.Position);
    Assert.Equal(0, battle.Events.EventsOf<ObjectInteractedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Foreign object proofs interrupt instead of touching local occupancy")]
  public void ForeignObjectProofIsInterrupted()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var local = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    var localUnit = local.Spawn(TestData.MakeCombatant("A", player), new Vector3I(1, 0, 1));
    local.Spawn(TestData.MakeCombatant("B", enemy), new Vector3I(3, 0, 3));
    BattleBoardState.ValidatedPoint localPoint = local.At(new Vector3I(2, 0, 1));
    var localBomb = local.PlaceObject(MakeInteractiveObjectData(), localPoint.Raw);

    using var foreign = new BattleFixture(new Vector3I(4, 1, 4), [TestData.MakeFaction("P"), TestData.MakeFaction("E")]);
    foreign.Spawn(TestData.MakeCombatant("A", foreign.PlayerFaction), new Vector3I(1, 0, 1));
    foreign.Spawn(TestData.MakeCombatant("B", foreign.EnemyFaction), new Vector3I(3, 0, 3));
    BattleBoardState.ValidatedPoint foreignPoint = foreign.At(new Vector3I(2, 0, 1));
    var foreignBomb = foreign.PlaceObject(MakeInteractiveObjectData(), foreignPoint.Raw);
    local.Start();
    foreign.Start();

    LiveObject foreignProof = foreign.Live(foreignBomb);

    // The foreign proof is stale inside the local battle, so the submission interrupts
    // quietly: no interaction event, no local occupancy change.
    local.Submit(BattleAction.InteractWithObject(local.Alive(localUnit), foreignProof));

    Assert.Equal(0, local.Events.EventsOf<ObjectInteractedBattleEvent>().AsValueEnumerable().Count());
    Assert.True(localBomb.Status.IsNone);
    Assert.True(local.Session.TryGetAliveObject(localBomb).IsSome);
    Assert.True(local.Board.IsBlockedByObject(localPoint));
    Assert.Equal(localPoint.Raw, localBomb.Position);
    Assert.True(foreignBomb.Status.IsNone);
  }

  [TestCase(TestName = "A defused object keeps stale proofs from executing")]
  public void StaleProofIsInterrupted()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    var unit = battle.Spawn(TestData.MakeCombatant("A", player), new Vector3I(1, 0, 1));
    battle.Spawn(TestData.MakeCombatant("B", enemy), new Vector3I(3, 0, 3));
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 1));
    var bomb = battle.PlaceObject(MakeInteractiveObjectData(), point.Raw);
    battle.Start();

    AliveUnit unitProof = battle.Alive(unit);
    LiveObject objectProof = battle.Live(bomb);

    battle.Submit(BattleAction.InteractWithObject(unitProof, objectProof));

    Assert.True(battle.Session.TryGetAliveObject(bomb).IsNone);
    int apAfterInteraction = unit.CurrentActionPoints;

    // The saved object proof went stale at defusal: a second submission with it interrupts
    // quietly — no AP cost, no second interaction event, terminal status/occupancy unchanged.
    battle.Submit(BattleAction.InteractWithObject(unitProof, objectProof));

    Assert.Equal(apAfterInteraction, unit.CurrentActionPoints);
    Assert.Equal(Some(ObjectStatus.Interacted), bomb.Status);
    Assert.True(battle.Session.TryGetAliveObject(bomb).IsNone);
    Assert.False(battle.Board.IsBlockedByObject(point));
    Assert.Equal(point.Raw, bomb.Position);
    Assert.Equal(1, battle.Events.EventsOf<ObjectInteractedBattleEvent>().AsValueEnumerable().Count());
  }
}
