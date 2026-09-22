using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleStartupLifecycleTest
{
  // Two sides, one unit each, distinct objective resources (the player side carries two so
  // objective-order assertions are meaningful), seed fixed for determinism.
  private static (BattleSetup Setup, Faction Player, Faction Enemy) GroupedSetup()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(),
      [
        new BattleSideSetup(player,
          [new FakeObjectiveData(), new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", player)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemy,
          [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3))]),
      ],
      Seed: 7,
      PlayerFaction: Some(player));
    return (setup, player, enemy);
  }

  [TestCase]
  public void DeclaredSystemObservesFirstTurn()
  {
    var type = TestData.MakeDuelBattleType();
    var hook = new RecordingHook();
    type.Systems.Add(new SetupSystemData
    {
      OnRegister = runtime => runtime.RegisterHook<BattleEventTag>(hook),
    });
    using var runtime = BattleFactory.Start(type, seed: 7).RequireRight();
    Assert.Equal(1, hook.Received.EventsOf<TurnStartedBattleEvent>().Length);
  }

  [TestCase(TestName = "Declared systems observe spawns, object placement, session start, and the first turn")]
  public void DeclaredSystemObservesStartupEvents()
  {
    var (setup, _, _) = GroupedSetup();
    var hook = new RecordingHook();
    var system = new SetupSystemData
    {
      OnRegister = runtime => runtime.RegisterHook<BattleEventTag>(hook),
    };
    using var runtime = BattleFactory.Start(setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" },
        new Vector3I(1, 0, 0))],
      Systems = [system],
    }).RequireRight();

    var added = hook.Received.EventsOf<UnitAddedBattleEvent>();
    Assert.Equal(2, added.Length);
    Assert.True(ReferenceEquals(setup.Sides[0].Faction, added[0].Unit.Side));
    Assert.True(ReferenceEquals(setup.Sides[1].Faction, added[1].Unit.Side));
    Assert.Equal(1, hook.Received.EventsOf<ObjectPlacedBattleEvent>().Length);
    hook.Received.EventBefore<UnitAddedBattleEvent, ObjectPlacedBattleEvent>();
    hook.Received.EventBefore<ObjectPlacedBattleEvent, SessionStartedBattleEvent>();
    hook.Received.EventBefore<SessionStartedBattleEvent, TurnStartedBattleEvent>();
    Assert.True(ReferenceEquals(setup.Sides[0].Faction,
      hook.Received.SingleEvent<TurnStartedBattleEvent>().Faction));
    var order = runtime.Query(new GetGlobalFactionTurnOrderQuery());
    for (int sideIndex = 0; sideIndex < setup.Sides.Count; sideIndex++)
    {
      BattleSideSetup side = setup.Sides[sideIndex];
      Assert.True(ReferenceEquals(side.Faction, order[sideIndex]));
      var objectives = runtime.Query(new GetObjectivesForFaction(side.Faction)).RequireSome();
      Assert.Equal(side.Objectives.Count, objectives.Count);
      for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
        Assert.True(ReferenceEquals(side.Objectives[objectiveIndex], objectives[objectiveIndex].Data));
    }
  }

  [TestCase(TestName = "A throwing declared-system registration preserves the exception and disposes the runtime")]
  public void ThrowingRegistrationDisposesRuntime()
  {
    var (setup, _, _) = GroupedSetup();
    var expected = new InvalidOperationException("setup sentinel");
    var system = new SetupSystemData { OnRegister = _ => throw expected };
    Exception? observed = null;
    try
    {
      using var unexpected = BattleFactory.Start(setup with { Systems = [system] }).RequireRight();
    }
    catch (Exception error)
    {
      observed = error;
    }
    Assert.True(ReferenceEquals(expected, observed));
    Assert.True(system.RegisteredRuntime is not null);
    Assert.Throws<ObjectDisposedException>(() =>
      system.RegisteredRuntime!.Query(new GetBattlePhaseQuery()));
  }

  [TestCase(TestName = "A unit-added hook fault preserves the exception and disposes the runtime")]
  public void SpawnHookFaultDisposesRuntime()
  {
    var (setup, _, _) = GroupedSetup();
    AssertStartupFaultDisposes(setup, typeof(UnitAddedBattleEvent));
  }

  [TestCase(TestName = "A session-start hook fault preserves the exception and disposes the runtime")]
  public void StartHookFaultDisposesRuntime()
  {
    var (setup, _, _) = GroupedSetup();
    AssertStartupFaultDisposes(setup, typeof(SessionStartedBattleEvent));
  }

  private static void AssertStartupFaultDisposes(BattleSetup setup, Type eventType)
  {
    var expected = new InvalidOperationException("setup sentinel");
    var system = new SetupSystemData
    {
      OnRegister = runtime => runtime.RegisterHook<BattleEventTag>(
        new ThrowOnSetupEvent(eventType, expected)),
    };
    Exception? observed = null;
    try
    {
      using var unexpected = BattleFactory.Start(setup with { Systems = [system] }).RequireRight();
    }
    catch (Exception error)
    {
      observed = error;
    }
    Assert.True(ReferenceEquals(expected, observed));
    Assert.True(system.RegisteredRuntime is not null);
    Assert.Throws<ObjectDisposedException>(() =>
      system.RegisteredRuntime!.Query(new GetBattlePhaseQuery()));
  }

  [TestCase(TestName = "A malformed spawn buff throws, preserves the cause, and disposes the runtime")]
  public void MalformedSpawnBuffDisposesRuntime()
  {
    var (setup, player, _) = GroupedSetup();
    var broken = setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Units = [new UnitPlacement(
            new UnitLoadout(TestData.MakeCombatant("Alpha", player,
              buffs: [TestData.MakeBuff("Broken", null!)])),
            new Vector3I(0, 0, 0))],
        },
        setup.Sides[1],
      ],
    };
    var system = new SetupSystemData();
    Exception? observed = null;
    try
    {
      using var unexpected = BattleFactory.Start(broken with { Systems = [system] }).RequireRight();
    }
    catch (Exception error)
    {
      observed = error;
    }
    Assert.True(observed is InvalidOperationException);
    Assert.True(observed!.Message.Contains("no activation condition"));
    Assert.True(system.RegisteredRuntime is not null);
    Assert.Throws<ObjectDisposedException>(() =>
      system.RegisteredRuntime!.Query(new GetBattlePhaseQuery()));
  }

  [TestCase(TestName = "A battle with objectives but no units throws and disposes the runtime")]
  public void EmptyRostersThrowAndDispose()
  {
    var (setup, _, _) = GroupedSetup();
    var empty = setup with
    {
      Sides =
      [
        setup.Sides[0] with { Units = [] },
        setup.Sides[1] with { Units = [] },
      ],
    };
    var system = new SetupSystemData();
    Exception? observed = null;
    try
    {
      using var unexpected = BattleFactory.Start(empty with { Systems = [system] }).RequireRight();
    }
    catch (Exception error)
    {
      observed = error;
    }
    Assert.True(observed is not null);
    Assert.True(system.RegisteredRuntime is not null);
    Assert.Throws<ObjectDisposedException>(() =>
      system.RegisteredRuntime!.Query(new GetBattlePhaseQuery()));
  }

  [TestCase(TestName = "Repeated starts from one setup get fresh board, objective, object, and hook state")]
  public void IndependentStartsDoNotShareMutableState()
  {
    var (setup, _, _) = GroupedSetup();
    var hooks = new List<RecordingHook>();
    var system = new SetupSystemData
    {
      OnRegister = runtime =>
      {
        var hook = new RecordingHook();
        hooks.Add(hook);
        runtime.RegisterHook<BattleEventTag>(hook);
      },
    };
    var reusable = setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" },
        new Vector3I(1, 0, 0))],
      Systems = [system],
    };
    using var first = BattleFactory.Start(reusable).RequireRight();
    using var second = BattleFactory.Start(reusable).RequireRight();
    var point = new Vector3I(0, 0, 0);
    var firstUnit = first.Query(new GetUnitAtTile(first.TryGetTile(point).RequireSome())).RequireSome();
    var secondUnit = second.Query(new GetUnitAtTile(second.TryGetTile(point).RequireSome())).RequireSome();
    Assert.False(ReferenceEquals(firstUnit, secondUnit));
    Assert.True(ReferenceEquals(firstUnit.Combatant, secondUnit.Combatant));
    var faction = setup.Sides[0].Faction;
    var firstObjective = first.Query(new GetObjectivesForFaction(faction)).RequireSome()[0];
    var secondObjective = second.Query(new GetObjectivesForFaction(faction)).RequireSome()[0];
    Assert.False(ReferenceEquals(firstObjective, secondObjective));
    var firstObjects = first.Query(new GetBattleSpecialObjectsQuery());
    var secondObjects = second.Query(new GetBattleSpecialObjectsQuery());
    Assert.Equal(1, firstObjects.Count);
    Assert.Equal(1, secondObjects.Count);
    Assert.False(ReferenceEquals(firstObjects[0], secondObjects[0]));
    Assert.False(ReferenceEquals(hooks[0], hooks[1]));
    int secondEventCount = hooks[1].Received.Count;
    first.ExecuteAction(BattleAction.ApplyDamage(first.TryGetAlive(firstUnit).RequireSome(), 1));
    Assert.Equal(secondUnit.MaxHealth, secondUnit.CurrentHealth);
    Assert.Equal(secondEventCount, hooks[1].Received.Count);
  }

  [TestCase(TestName = "A terminal first-turn objective ends the battle during startup")]
  public void TerminalObjectiveEndsBattleDuringStartup()
  {
    var (setup, _, _) = GroupedSetup();
    var terminal = setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Objectives = [new SurviveUntilTurnObjectiveData
          {
            TargetTurn = 1,
            OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
          }],
        },
        setup.Sides[1],
      ],
    };
    using var runtime = BattleFactory.Start(terminal).RequireRight();
    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(BattleOutcome.Victory, runtime.Query(new GetBattleResultQuery()).RequireRight().Outcome);
  }

  [TestCase(TestName = "Seed 7 produces the same combat rolls from the type and resolved setups")]
  public void SeedDrivesIdenticalCombatRolls()
  {
    var type = TestData.MakeDuelBattleType();
    type.MapPool.Add(TestData.MakeMapScene(TestData.MakeMapData(new Vector3I(2, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(1)))));
    BattleSetup resolved = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();

    using BattleRuntime fromType = BattleFactory.Start(type, seed: 7).RequireRight();
    using BattleRuntime fromSetup = BattleFactory.Start(resolved).RequireRight();

    int expectedRoll = new Random(7).Next(100);
    Assert.Equal(expectedRoll, FirstAttackEvent(fromType).Roll);
    Assert.Equal(expectedRoll, FirstAttackEvent(fromSetup).Roll);
  }

  [TestCase(TestName = "Optional hit-calculator injection survives the model change")]
  public void HitCalculatorInjectionReachesCombat()
  {
    BattleSetup resolved = BattleSetupResolver.Resolve(
      TestData.MakeDuelBattleType(), seed: 7).RequireRight();
    using var runtime = BattleFactory.Start(resolved, new AlwaysHitCalculator()).RequireRight();
    Assert.Equal(100, FirstAttackEvent(runtime).Breakdown.FinalChance);
  }

  private static UnitAttackedBattleEvent FirstAttackEvent(BattleRuntime runtime)
  {
    var attacker = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();
    var target = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(1, 0, 0)).RequireSome())).RequireSome();
    var result = runtime.ExecuteAction(BattleAction.AttackEntity(
      runtime.TryGetAlive(attacker).RequireSome(),
      runtime.TryGetAttackTarget(new BattleEntity.Unit(target)).RequireSome()));
    foreach (BattleEvent battleEvent in result.EventsThatOccurred)
      if (battleEvent is UnitAttackedBattleEvent attacked)
        return attacked;
    throw new InvalidOperationException("Expected an attack event.");
  }
}
