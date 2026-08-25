using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class DefuseAllBombsObjectiveTest
{
  private static BattleRuntime UnwrapStart(Either<BattleSetupFailure, BattleRuntime> result) =>
    result.Match(
      Right: runtime => runtime,
      Left: failure => throw new InvalidOperationException($"Expected Start to succeed but got {failure.Reason}: {failure.Message}"));

  private static BattleSpecialObjectData MakeBomb()
  {
    var data = new BattleSpecialObjectData { Name = "Bomb" };
    data.Capabilities.Add(new InteractiveCapabilityData());
    data.Capabilities.Add(new TimedEffectCapabilityData { FireAfterTurns = 3, EffectRadius = 0 });
    return data;
  }

  private static BattleSpecialObjectData MakeCrate()
  {
    // Interactable but not timed: interacting with it must not count toward the defuse goal.
    var data = new BattleSpecialObjectData { Name = "Crate" };
    data.Capabilities.Add(new InteractiveCapabilityData());
    return data;
  }

  private static (BattleRuntime Runtime, Faction Player) BattleWithDefuseGoal(
    IReadOnlyList<ObjectPlacement>? objects = null)
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    var setup = new BattleSetup(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
      {
        [player] = [new DefuseAllBombsObjectiveData
        {
          OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
          OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
        }],
        [enemy] = [new FakeObjectiveData()],
      },
      PlayerFaction: Some(player),
      Objects: objects ?? [new ObjectPlacement(MakeBomb(), new Vector3I(1, 0, 0))]);

    return (UnwrapStart(BattleFactory.Start(setup)), player);
  }

  [TestCase(TestName = "Defusing every bomb passes and ends the battle in victory")]
  public void AllDefusedPasses()
  {
    var (runtime, player) = BattleWithDefuseGoal();
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];
    BattleUnitState defuser = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser).RequireSome(),
      runtime.TryGetAliveObject(bomb).RequireSome()));

    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(BattleOutcome.Victory, GetValue(runtime.Query(new GetFactionEndOfBattleSummary(player))).Outcome);
  }

  [TestCase(TestName = "A expiry fails the objective and ends the battle in defeat")]
  public void ExpiryFails()
  {
    var (runtime, player) = BattleWithDefuseGoal();
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];

    for (int turn = 0; turn < 6 && runtime.Query(new GetBattlePhaseQuery()) == BattlePhase.InProgress; turn++)
      runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Assert.Equal(Some(ObjectStatus.Expired), bomb.Status);
    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(BattleOutcome.Defeat, GetValue(runtime.Query(new GetFactionEndOfBattleSummary(player))).Outcome);
  }

  // The counting implementation's distinguishing cases: only the delivered stream counts,
  // so one defused of two stays Ongoing, and non-timed objects never enter the tally.

  [TestCase(TestName = "Defusing one of two bombs stays ongoing")]
  public void PartialDefusalStaysOngoing()
  {
    var (runtime, _) = BattleWithDefuseGoal(
    [
      new ObjectPlacement(MakeBomb(), new Vector3I(1, 0, 0)),
      new ObjectPlacement(MakeBomb(), new Vector3I(2, 0, 2)),
    ]);
    var bombs = runtime.Query(new GetBattleSpecialObjectsQuery());
    BattleUnitState defuser = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser).RequireSome(),
      runtime.TryGetAliveObject(bombs[0]).RequireSome()));

    Assert.Equal(BattlePhase.InProgress, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(Some(ObjectStatus.Interacted), bombs[0].Status);
    Assert.True(bombs[1].Status.IsNone);
  }

  [TestCase(TestName = "Interacting with a non-timed object does not count as a defusal")]
  public void NonTimedObjectsDoNotCount()
  {
    var (runtime, _) = BattleWithDefuseGoal(
    [
      new ObjectPlacement(MakeBomb(), new Vector3I(1, 0, 0)),
      new ObjectPlacement(MakeCrate(), new Vector3I(2, 0, 2)),
    ]);
    var objects = runtime.Query(new GetBattleSpecialObjectsQuery());
    BattleObjectState crate = objects.AsValueEnumerable().Single(obj => obj.Name == "Crate");
    BattleUnitState defuser = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser).RequireSome(),
      runtime.TryGetAliveObject(crate).RequireSome()));

    Assert.Equal(BattlePhase.InProgress, runtime.Query(new GetBattlePhaseQuery()));
  }
}
