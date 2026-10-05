using FunProject.Battle;
using FunProject.Combatants;
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

  // Factory-ready fixture: unit A at (1,0,1), unit B at (3,0,3), one interactive bomb at (2,0,1).
  private static BattleFixture BombFixture(Faction player, Faction enemy, int playerActionPoints = 4)
    => new(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [
        new BattleSideSetup(player, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player,
            actionPoints: playerActionPoints)), new Vector3I(1, 0, 1))]),
        new BattleSideSetup(enemy, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3))]),
      ],
      Seed: 7) with
    { Objects = [new ObjectPlacement(MakeInteractiveObjectData(), new Vector3I(2, 0, 1))] });

  private static BattleObjectState BombAt(BattleFixture battle, Vector3I cell) =>
    battle.Query(new GetBattleSpecialObjectsQuery()).AsValueEnumerable().Single(o => o.Position == cell);

  [TestCase(TestName = "Adjacent interact spends AP, defuses, clears occupancy, raises event")]
  public void HappyPathInteract()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = BombFixture(player, enemy);
    var unit = battle.UnitAt(new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 1));
    var bomb = BombAt(battle, point.Raw);
    battle.ClearEvents();

    int apBefore = unit.CurrentActionPoints;
    battle.Interact(unit, bomb);

    Assert.Equal(Some(ObjectStatus.Interacted), bomb.Status);
    Assert.Equal(apBefore - 2, unit.CurrentActionPoints);
    Assert.True(battle.Session.TryGetAliveObject(bomb).IsNone);
    Assert.False(battle.Session.Board.IsBlockedByObject(point));
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
      configure: setup => setup with
      {
        Objects = [new ObjectPlacement(MakeInteractiveObjectData(actionPointCost: 2), new Vector3I(5, 0, 1))],
      });
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(5, 0, 1));
    BattleObjectState bomb = BombAt(battle, point.Raw);

    IReadOnlyList<UnitAction> playerActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    IReadOnlyList<UnitAction> enemyActions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.EnemyUnit)));
    foreach (UnitAction action in playerActions)
      _ = action.IsAvailable;
    foreach (UnitAction action in enemyActions)
      _ = action.IsAvailable;
    int turnNumber = battle.Session.TurnNumber;

    battle.Interact(battle.PlayerUnit, bomb);

    Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints);
    Assert.Equal(BattlePhase.InProgress, battle.Query(new GetBattlePhaseQuery()));
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
    Assert.Equal(turnNumber, battle.Session.TurnNumber);
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
    using var battle = BombFixture(player, enemy, playerActionPoints: 1);
    var unit = battle.UnitAt(new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 1));
    var bomb = BombAt(battle, point.Raw);
    battle.ClearEvents();

    Assert.Throws<InvalidOperationException>(() => battle.Submit(
      BattleAction.InteractWithObject(battle.Alive(unit), battle.Live(bomb))));

    Assert.True(bomb.Status.IsNone);
    Assert.True(battle.Session.TryGetAliveObject(bomb).IsSome);
    Assert.True(battle.Session.Board.IsBlockedByObject(point));
    Assert.Equal(point.Raw, bomb.Position);
    Assert.Equal(0, battle.Events.EventsOf<ObjectInteractedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Foreign object proofs interrupt instead of touching local occupancy")]
  public void ForeignObjectProofIsInterrupted()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var local = BombFixture(player, enemy);
    var localUnit = local.UnitAt(new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint localPoint = local.At(new Vector3I(2, 0, 1));
    var localBomb = BombAt(local, localPoint.Raw);

    using var foreign = BombFixture(TestData.MakeFaction("P"), TestData.MakeFaction("E"));
    BattleBoardState.ValidatedPoint foreignPoint = foreign.At(new Vector3I(2, 0, 1));
    var foreignBomb = BombAt(foreign, foreignPoint.Raw);

    LiveObject foreignProof = foreign.Live(foreignBomb);

    Assert.Equal(
      BattleAction.Result.Interrupted,
      BattleAction.InteractWithObject(local.Alive(localUnit), foreignProof).Execute(local.Session));

    Assert.True(localBomb.Status.IsNone);
    Assert.True(local.Session.TryGetAliveObject(localBomb).IsSome);
    Assert.True(local.Session.Board.IsBlockedByObject(localPoint));
    Assert.Equal(localPoint.Raw, localBomb.Position);
    Assert.True(foreignBomb.Status.IsNone);
  }

  [TestCase(TestName = "A defused object keeps stale proofs from executing")]
  public void StaleProofIsInterrupted()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = BombFixture(player, enemy);
    var unit = battle.UnitAt(new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 1));
    var bomb = BombAt(battle, point.Raw);

    AliveUnit unitProof = battle.Alive(unit);
    LiveObject objectProof = battle.Live(bomb);

    battle.Submit(BattleAction.InteractWithObject(unitProof, objectProof));

    Assert.True(battle.Session.TryGetAliveObject(bomb).IsNone);
    Assert.Equal(BattleAction.Result.Interrupted,
      BattleAction.InteractWithObject(unitProof, objectProof).Execute(battle.Session));
  }
}
