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

  private static BattleRuntime RuntimeWithBomb(Vector3I bombCell, BattleSpecialObjectData bombData)
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = [new FakeObjectiveData()],
      [enemy] = [new FakeObjectiveData()],
    };

    return BattleFactory.Start(new BattleSetup(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      objectives,
      Objects:
      [
        new ObjectPlacement(bombData, bombCell),
      ])).Match(
      Right: runtime => runtime,
      Left: failure => throw new InvalidOperationException($"Expected runtime but got {failure.Reason}: {failure.Message}"));
  }

  [TestCase(TestName = "Turn end at the deadline expires: damage applied, event raised, defused bombs skipped")]
  public void ExpiryAtDeadline()
  {
    BattleRuntime runtime = RuntimeWithBomb(new Vector3I(1, 0, 0), MakeBomb(expireAfterTurns: 1));
    var events = new System.Collections.Generic.List<BattleEvent>();
    runtime.BattleEventCommitted += events.Add;
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());

    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];
    BattleTestUnit victim = new(runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome());
    int hpBefore = victim.State.CurrentHealth;

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Assert.Equal(Some(ObjectStatus.Expired), bomb.Status);
    Assert.Equal(new Vector3I(1, 0, 0), bomb.Position);
    Assert.True(events.AsValueEnumerable().OfType<ObjectExpiredBattleEvent>().Any());
    Assert.True(hpBefore > victim.State.CurrentHealth);
  }

  [TestCase(TestName = "Player wipe defeat stays stable when the defuse objective also fails in the same expiry dispatch")]
  public void ExpiryPlayerWipeDefeatHasStableOutcome()
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] =
      [
        new DefuseAllBombsObjectiveData
        {
          OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
        }
      ],
      [enemy] = [new FakeObjectiveData()],
    };

    using BattleRuntime runtime = BattleFactory.Start(new BattleSetup(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("A", player, health: 6)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      objectives,
      PlayerFaction: Some(player),
      Objects:
      [
        new ObjectPlacement(MakeBomb(expireAfterTurns: 1, baseDamage: 8), new Vector3I(1, 0, 0)),
      ])).Match(
      Right: started => started,
      Left: failure => throw new InvalidOperationException($"Expected runtime but got {failure.Reason}: {failure.Message}"));
    var recorder = new BattleEventRecorder(runtime);
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(
      BattleOutcome.Defeat,
      runtime.Query(new GetBattleResultQuery()).Match(
        Left: failure => throw new InvalidOperationException(failure.Message),
        Right: result => result.Outcome));
    Assert.Equal(1, recorder.OfType<SessionEndedBattleEvent>().AsValueEnumerable().Count());
    Assert.True(recorder.OfType<ObjectExpiredBattleEvent>().AsValueEnumerable().Any());
    Assert.True(recorder.OfType<ObjectiveFailedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Defused bombs do not expire")]
  public void DefusedSkipsExpiry()
  {
    BattleRuntime runtime = RuntimeWithBomb(new Vector3I(1, 0, 0), MakeBomb(expireAfterTurns: 1));
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];
    BattleTestUnit defuser = new(runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome());

    runtime.ExecuteAction(BattleAction.InteractWithObject(
      runtime.TryGetAlive(defuser.State).RequireSome(),
      runtime.TryGetAliveObject(bomb).RequireSome()));
    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Assert.Equal(Some(ObjectStatus.Interacted), bomb.Status);
  }

  [TestCase(TestName = "Expiry fires after the -100 upkeep systems")]
  public void ExpiryRunsAfterUpkeep()
  {
    var order = new System.Collections.Generic.List<string>();
    BattleRuntime runtime = RuntimeWithBomb(new Vector3I(1, 0, 1), MakeBomb(expireAfterTurns: 1));
    runtime.RegisterHook<TurnEndedBattleEvent>(new MarkerHook(() => order.Add("upkeep")), -100);
    runtime.RegisterHook<TurnEndedBattleEvent>(new MarkerHook(() => order.Add("expiry")), 0);

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

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
}
