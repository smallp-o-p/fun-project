using FunProject.Battle;
using GdUnit4;
using Godot;
using System;

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

  private static (BattleSession Session, BattleActionExecutor Executor, BattleTestUnit Unit, BattleObjectState Object, BattleBoardState.ValidatedPoint ObjectPoint) ArmedBattle(
    int unitActionPoints = 4,
    int actionPointCost = 2)
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    BattleSession session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player, enemy]);
    BattleActionExecutor executor = BattleActionTestHelper.ExecutorFor(session);

    var unit = BattleActionTestHelper.SpawnUnit(session,
      BattleTestFactory.MakeCombatant("A", player, actionPoints: unitActionPoints),
      new Vector3I(1, 0, 1));

    var point = session.Board.At(new Vector3I(2, 0, 1));
    executor.Submit(BattleAction.PlaceObject(MakeInteractiveObjectData(actionPointCost), point));
    var bomb = session.Objects.AsValueEnumerable().Single();

    return (session, executor, unit, bomb, point);
  }

  [TestCase(TestName = "Adjacent interact spends AP, defuses, clears occupancy, raises event")]
  public void HappyPathInteract()
  {
    var (session, executor, unit, bomb, point) = ArmedBattle();
    var recorder = new BattleEventRecorder(session);

    int apBefore = unit.State.CurrentActionPoints;
    executor.Submit(BattleAction.InteractWithObject(
      session.TryGetAlive(unit.State).RequireSome(),
      session.TryGetAliveObject(bomb).RequireSome()));

    Assert.Equal(Some(ObjectStatus.Interacted), bomb.Status);
    Assert.Equal(apBefore - 2, unit.State.CurrentActionPoints);
    Assert.True(session.TryGetAliveObject(bomb).IsNone);
    Assert.False(session.Board.IsBlockedByObject(point));
    Assert.Equal(point.Raw, bomb.Position);

    ObjectInteractedBattleEvent @event = recorder.Single<ObjectInteractedBattleEvent>();
    Assert.Equal(unit.State, @event.Actor);
    Assert.Equal(bomb, @event.Object);
    Assert.Equal(point, @event.Position);
  }

  [TestCase(TestName = "Insufficient action points throw before object mutation")]
  public void InsufficientActionPointsDoNotMutateObject()
  {
    var (session, executor, unit, bomb, point) = ArmedBattle(unitActionPoints: 1, actionPointCost: 2);
    var recorder = new BattleEventRecorder(session);

    Assert.Throws<InvalidOperationException>(() => executor.Submit(
      BattleAction.InteractWithObject(
        session.TryGetAlive(unit.State).RequireSome(),
        session.TryGetAliveObject(bomb).RequireSome())));

    Assert.True(bomb.Status.IsNone);
    Assert.True(session.TryGetAliveObject(bomb).IsSome);
    Assert.True(session.Board.IsBlockedByObject(point));
    Assert.Equal(point.Raw, bomb.Position);
    Assert.Equal(0, recorder.OfType<ObjectInteractedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Foreign object proofs interrupt instead of touching local occupancy")]
  public void ForeignObjectProofIsInterrupted()
  {
    var (localSession, _, localUnit, localBomb, localPoint) = ArmedBattle();
    var (foreignSession, _, _, foreignBomb, _) = ArmedBattle();
    LiveObject foreignProof = foreignSession.TryGetAliveObject(foreignBomb).RequireSome();

    Assert.Equal(
      BattleAction.Result.Interrupted,
      BattleAction.InteractWithObject(
        localSession.TryGetAlive(localUnit.State).RequireSome(),
        foreignProof).Execute(localSession));

    Assert.True(localBomb.Status.IsNone);
    Assert.True(localSession.TryGetAliveObject(localBomb).IsSome);
    Assert.True(localSession.Board.IsBlockedByObject(localPoint));
    Assert.Equal(localPoint.Raw, localBomb.Position);
    Assert.True(foreignBomb.Status.IsNone);
  }

  [TestCase(TestName = "A defused object keeps stale proofs from executing")]
  public void StaleProofIsInterrupted()
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    BattleSession session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player, enemy]);
    BattleActionExecutor executor = BattleActionTestHelper.ExecutorFor(session);

    var unit = BattleActionTestHelper.SpawnUnit(session,
      BattleTestFactory.MakeCombatant("A", player),
      new Vector3I(1, 0, 1));

    var point = session.Board.At(new Vector3I(2, 0, 1));
    executor.Submit(BattleAction.PlaceObject(MakeInteractiveObjectData(), point));
    var bomb = session.Objects.AsValueEnumerable().Single();

    AliveUnit unitProof = session.TryGetAlive(unit.State).RequireSome();
    LiveObject objectProof = session.TryGetAliveObject(bomb).RequireSome();

    executor.Submit(BattleAction.InteractWithObject(unitProof, objectProof));

    Assert.True(session.TryGetAliveObject(bomb).IsNone);
    Assert.Equal(BattleAction.Result.Interrupted,
      BattleAction.InteractWithObject(unitProof, objectProof).Execute(session));
  }
}
