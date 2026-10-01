using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BombExpiryTest
{
  private static BattleSpecialObjectData MakeBomb(int expireAfterTurns, int baseDamage = 8)
  {
    var data = new BattleSpecialObjectData { Name = "Bomb" };
    data.Capabilities.Add(new InteractiveCapabilityData());
    var trigger = new TimedEffectCapabilityData
    {
      FireAfterTurns = expireAfterTurns,
      EffectRadius = 1,
    };
    trigger.Effects.Add(new DamageEffectData { BaseDamage = baseDamage });
    data.Capabilities.Add(trigger);
    return data;
  }

  private static BattleSetup BombSetup(Vector3I bombCell, BattleSpecialObjectData bombData)
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");

    return TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("A", player)), new UnitLoadout(TestData.MakeCombatant("B", enemy)),
      [new FakeObjectiveData()], [new FakeObjectiveData()])
      with
    { Objects = [new ObjectPlacement(bombData, bombCell)] };
  }

  [TestCase(TestName = "Turn end at the deadline expires: damage applied, event raised, defused bombs skipped")]
  public void ExpiryAtDeadline()
  {
    using var runtime = BattleFactory.Start(BombSetup(new Vector3I(1, 0, 0), MakeBomb(expireAfterTurns: 1))).RequireRight();
    var events = new System.Collections.Generic.List<BattleEvent>();
    runtime.BattleEventCommitted += events.Add;
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());

    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];
    BattleUnitState victim = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();
    int hpBefore = victim.CurrentHealth;

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction));

    Assert.Equal(Some(ObjectStatus.Expired), bomb.Status);
    Assert.Equal(new Vector3I(1, 0, 0), bomb.Position);
    Assert.True(events.AsValueEnumerable().OfType<ObjectExpiredBattleEvent>().Any());
    Assert.True(hpBefore > victim.CurrentHealth);
  }

  [TestCase(TestName = "Player wipe defeat stays stable when the defuse objective also fails in the same expiry dispatch")]
  public void ExpiryPlayerWipeDefeatHasStableOutcome()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");

    using var runtime = BattleFactory.Start(TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("A", player, health: 6)), new UnitLoadout(TestData.MakeCombatant("B", enemy)),
      [new DefuseAllBombsObjectiveData
      {
        OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
      }],
      [new FakeObjectiveData()], Some(player))
      with
    {
      Objects =
        [
          new ObjectPlacement(MakeBomb(expireAfterTurns: 1, baseDamage: 8), new Vector3I(1, 0, 0)),
        ],
    }).RequireRight();
    List<BattleEvent> events = [];
    runtime.BattleEventCommitted += events.Add;
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction));

    Assert.True(runtime.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(
      BattleOutcome.Defeat,
      runtime.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(1, events.AsValueEnumerable().OfType<SessionEndedBattleEvent>().Count());
    Assert.True(events.AsValueEnumerable().OfType<ObjectExpiredBattleEvent>().Any());
    Assert.True(events.AsValueEnumerable().OfType<ObjectiveFailedBattleEvent>().Any());
  }

  [TestCase(TestName = "Defused bombs do not expire")]
  public void DefusedSkipsExpiry()
  {
    using var runtime = BattleFactory.Start(BombSetup(new Vector3I(1, 0, 0), MakeBomb(expireAfterTurns: 1))).RequireRight();
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];
    BattleUnitState defuser = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser).RequireSome(),
      runtime.TryGetAliveObject(bomb).RequireSome()));
    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction));

    Assert.Equal(Some(ObjectStatus.Interacted), bomb.Status);
  }

  [TestCase(TestName = "Expiry fires after the -100 upkeep systems")]
  public void ExpiryRunsAfterUpkeep()
  {
    var order = new System.Collections.Generic.List<string>();
    using var runtime = BattleFactory.Start(BombSetup(new Vector3I(1, 0, 1), MakeBomb(expireAfterTurns: 1))).RequireRight();
    runtime.RegisterHook<TurnEndedBattleEvent>(new MarkerHook(() => order.Add("upkeep")), -100);
    runtime.RegisterHook<TurnEndedBattleEvent>(new MarkerHook(() => order.Add("expiry")), 0);

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction));

    Assert.Equal("upkeep,expiry", string.Join(",", order));
  }

  private sealed class MarkerHook(System.Action onFire) : BattleHook
  {
    public override System.Collections.Generic.IReadOnlyList<BattleAction> OnEvent(
      HookContext context, BattleEvent battleEvent)
    {
      onFire();
      return [];
    }
  }

  [TestCase]
  public void ExpiryPayloadDamagesObjectsThroughTheSharedResolver()
  {
    using var battle = BattleFixture.Duel(start: false);
    var timer = new TimedEffectCapabilityData { FireAfterTurns = 1, EffectRadius = 1 };
    timer.Effects.Add(new DamageEffectData { BaseDamage = 8 });
    var bomb = battle.PlaceObject(TestData.MakeObject("Bomb", 20, timer), new Vector3I(1, 0, 1));
    var crate = battle.PlaceObject(TestData.MakeObject("Crate", 20), new Vector3I(2, 0, 1));
    battle.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());
    battle.Start();
    battle.ClearEvents();
    battle.AdvanceTurn();
    Assert.Equal(Some(ObjectStatus.Expired), bomb.Status);
    Assert.True(battle.Runtime.TryGetAttackTarget(new BattleEntity.Object(bomb)).IsNone);
    Assert.Equal(12, crate.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    battle.Events.EventBefore<ObjectDamagedBattleEvent, ObjectExpiredBattleEvent>();
  }

  [TestCase]
  public void DestroyedBombNeverFiresItsPayloadOrExpiresLater()
  {
    using var battle = BattleFixture.Duel(start: false,
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", damage: 5)));
    var timer = new TimedEffectCapabilityData { FireAfterTurns = 1, EffectRadius = 1 };
    timer.Effects.Add(new DamageEffectData { BaseDamage = 99 });
    var bomb = battle.PlaceObject(TestData.MakeObject("Bomb", 5, timer), new Vector3I(4, 0, 2));
    battle.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());
    battle.Start();
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, bomb);
    battle.AdvanceTurn();
    Assert.Equal(Some(ObjectStatus.Destroyed), bomb.Status);
    Assert.Equal(battle.PlayerUnit.MaxHealth, battle.PlayerUnit.CurrentHealth);
    Assert.Equal(0, battle.Events.EventsOf<ObjectExpiredBattleEvent>().Length);
    Assert.True(battle.Runtime.TryGetAttackTarget(new BattleEntity.Object(bomb)).IsNone);
  }
}
