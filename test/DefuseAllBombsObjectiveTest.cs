using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class DefuseAllBombsObjectiveTest
{
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

  private static (BattleSetup Setup, Faction Player) DefuseGoalSetup(
    IReadOnlyList<ObjectPlacement>? objects = null)
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(),
      [
        new BattleSideSetup(player,
          [new DefuseAllBombsObjectiveData
          {
            OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
            OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
          }],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemy, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3))]),
      ],
      Seed: 7,
      PlayerFaction: Some(player))
    {
      Objects = objects ?? [new ObjectPlacement(MakeBomb(), new Vector3I(1, 0, 0))],
    };

    return (setup, player);
  }

  [TestCase(TestName = "Defusing every bomb passes and ends the battle in victory")]
  public void AllDefusedPasses()
  {
    var (setup, player) = DefuseGoalSetup();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];
    BattleUnitState defuser = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser).RequireSome(),
      runtime.TryGetAliveObject(bomb).RequireSome()));

    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(BattleOutcome.Victory, runtime.Query(new GetFactionEndOfBattleSummary(player)).RequireRight().Outcome);
  }

  [TestCase(TestName = "A expiry fails the objective and ends the battle in defeat")]
  public void ExpiryFails()
  {
    var (setup, player) = DefuseGoalSetup();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];

    for (int turn = 0; turn < 6 && runtime.Query(new GetBattlePhaseQuery()) == BattlePhase.InProgress; turn++)
      runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Assert.Equal(Some(ObjectStatus.Expired), bomb.Status);
    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(BattleOutcome.Defeat, runtime.Query(new GetFactionEndOfBattleSummary(player)).RequireRight().Outcome);
  }

  // The counting implementation's distinguishing cases: only the delivered stream counts,
  // so one defused of two stays Ongoing, and non-timed objects never enter the tally.

  [TestCase(TestName = "Defusing one of two bombs stays ongoing")]
  public void PartialDefusalStaysOngoing()
  {
    var (setup, _) = DefuseGoalSetup(
    [
      new ObjectPlacement(MakeBomb(), new Vector3I(1, 0, 0)),
      new ObjectPlacement(MakeBomb(), new Vector3I(2, 0, 2)),
    ]);
    using var runtime = BattleFactory.Start(setup).RequireRight();
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
    var (setup, _) = DefuseGoalSetup(
    [
      new ObjectPlacement(MakeBomb(), new Vector3I(1, 0, 0)),
      new ObjectPlacement(MakeCrate(), new Vector3I(2, 0, 2)),
    ]);
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var objects = runtime.Query(new GetBattleSpecialObjectsQuery());
    BattleObjectState crate = objects.AsValueEnumerable().Single(obj => obj.Name == "Crate");
    BattleUnitState defuser = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser).RequireSome(),
      runtime.TryGetAliveObject(crate).RequireSome()));

    Assert.Equal(BattlePhase.InProgress, runtime.Query(new GetBattlePhaseQuery()));
  }

  [TestCase]
  public void DestroyingTimedObjectsFailsButDestroyingCratesDoesNot()
  {
    using var battle = BattleFixture.Duel(start: false, playerControlled: true,
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", damage: 5)));
    battle.Session.AddObjective(battle.PlayerFaction, new DefuseAllBombsObjectiveData
    {
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    }.Instantiate());
    var bomb = battle.PlaceObject(TestData.MakeObject("Bomb", 5,
      new TimedEffectCapabilityData { FireAfterTurns = 5 }), new Vector3I(3, 0, 2));
    var crate = battle.PlaceObject(TestData.MakeObject("Crate", 5), new Vector3I(2, 0, 2));
    battle.Start();
    battle.Attack(battle.PlayerUnit, crate);
    Assert.Equal(BattlePhase.InProgress, battle.Query(new GetBattlePhaseQuery()));
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, bomb);
    Assert.Equal(BattlePhase.Ended, battle.Query(new GetBattlePhaseQuery()));
    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetBattleResultQuery()).RequireRight().Outcome);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveFailedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<ObjectInteractedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<ObjectExpiredBattleEvent>().Length);
  }

  [TestCase]
  public void GrenadeDestructionFailsTheObjectiveAndFinishesResolution()
  {
    using var battle = BattleFixture.Duel(start: false, playerControlled: true);
    battle.Session.AddObjective(battle.PlayerFaction, new DefuseAllBombsObjectiveData
    {
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    }.Instantiate());
    var cell = new Vector3I(3, 0, 2);
    var bomb = battle.PlaceObject(TestData.MakeObject("Bomb", 5,
      new TimedEffectCapabilityData { FireAfterTurns = 5 }), cell);
    battle.Start();
    var grenade = TestData.MakeGrenade("Frag", throwRange: 10, blastRadius: 0,
      effects: [new DamageEffectData { BaseDamage = 5 }]);
    battle.PlayerUnit.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(battle.PlayerUnit, grenade, cell);
    Assert.Equal(Some(ObjectStatus.Destroyed), bomb.Status);
    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetBattleResultQuery()).RequireRight().Outcome);
    Assert.Equal(1, battle.Events.EventsOf<ObjectiveFailedBattleEvent>().Length);
    Assert.Equal(1, battle.Events.EventsOf<CapabilityResolvedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<ObjectInteractedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<ObjectExpiredBattleEvent>().Length);
  }
}
