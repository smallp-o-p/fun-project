using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items.Effects;
using FunProject.Stats;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleStartupLifecycleTest
{
  private static (BattleSetup Setup, Faction Player, Faction Enemy) GroupedSetup()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var setup = TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("Alpha", player)),
      new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)),
      // The player side carries two distinct objective resources so objective-order
      // assertions are meaningful.
      [new FakeObjectiveData(), new FakeObjectiveData()], [new FakeObjectiveData()], Some(player));
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

  [TestCase(TestName = "Declared systems register after preparation: they see the full roster, no placement replay, and start events in order")]
  public void DeclaredSystemObservesStartupEvents()
  {
    var (setup, _, _) = GroupedSetup();
    var hook = new RecordingHook();
    bool nestedAttempted = false;
    int startedCount = 0;
    int completedCount = 0;
    var system = new SetupSystemData
    {
      OnRegister = runtime =>
      {
        // Registration happens after complete placement: the declared system can already
        // read every initial unit and object through the runtime.
        Assert.Equal(2, runtime.Query(new GetGlobalFactionTurnOrderQuery()).Count);
        Assert.Equal(1, runtime.Query(new GetBattleSpecialObjectsQuery()).Count);
        runtime.RegisterHook<BattleEventTag>(hook);
        // Subscribed before the opening dispatch: the opening must fire no public pair.
        runtime.ActionStarted += _ => startedCount++;
        runtime.ActionCompleted += _ => completedCount++;
        // Both startup events share one unbroken opening dispatch scope, so the earliest
        // window edge (SessionStarted) rejects nested submissions for the whole opening.
        runtime.BattleEventCommitted += battleEvent =>
        {
          if (battleEvent is not SessionStartedBattleEvent)
            return;
          nestedAttempted = true;
          BattleUnitState registered = runtime.Query(new GetUnitAtTile(
            runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();
          Assert.Throws<InvalidOperationException>(() => runtime.ExecuteAction(BattleAction.ApplyDamage(
            runtime.TryGetAlive(registered).RequireSome(), 1)));
        };
      },
    };
    using var runtime = BattleFactory.Start(setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" },
        new Vector3I(1, 0, 0))],
      Systems = [system],
    }).RequireRight();

    // The nested ExecuteAction was rejected at the open submission window: no damage cost,
    // no lifecycle pair, and no committed work from the attempt. The opening itself
    // finished intact: full health and the full initial AP top-up still ran.
    Assert.True(nestedAttempted);
    BattleUnitState unit = runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();
    Assert.Equal(20, unit.CurrentHealth);
    Assert.Equal(0, hook.Received.EventsOf<UnitDamagedBattleEvent>().Length);
    Assert.Equal(0, startedCount);
    Assert.Equal(0, completedCount);
    Assert.Equal(4, unit.MaxActionPoints);
    Assert.Equal(4, unit.CurrentActionPoints);

    // Initial placement is preparation-owned: no replay reaches registered systems.
    Assert.Equal(0, hook.Received.EventsOf<UnitAddedBattleEvent>().Length);
    Assert.Equal(0, hook.Received.EventsOf<ObjectPlacedBattleEvent>().Length);
    Assert.Equal(1, hook.Received.EventsOf<SessionStartedBattleEvent>().Length);
    var started = hook.Received.EventsOf<TurnStartedBattleEvent>();
    Assert.Equal(1, started.Length);
    hook.Received.EventBefore<SessionStartedBattleEvent, TurnStartedBattleEvent>();
    Assert.True(ReferenceEquals(setup.Sides[0].Faction, started[0].Faction));

    // Runtime reinforcement commits UnitAdded through the same registration door.
    var reinforcement = TestData.MakeCombatant("Late", setup.Sides[0].Faction);
    runtime.ExecuteAction(BattleAction.SpawnUnit(reinforcement,
      runtime.TryGetTile(new Vector3I(2, 0, 2)).RequireSome()));
    Assert.Equal(1, hook.Received.EventsOf<UnitAddedBattleEvent>().Length);
    Assert.True(ReferenceEquals(reinforcement,
      hook.Received.EventsOf<UnitAddedBattleEvent>()[0].Unit.Combatant));

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

  // Starts the real factory inside a using so even a successful Start disposes its runtime.
  // A capture must exist (a successful Start fails this helper), the system must have received
  // the runtime, and that runtime must reject queries after disposal.
  private static Exception AssertStartupFailureDisposes(BattleSetup setup, SetupSystemData system)
  {
    var observed = Assert.Throws<Exception>(() =>
    {
      using var unexpected = BattleFactory.Start(setup with { Systems = [system] }).RequireRight();
    });
    Assert.True(system.RegisteredRuntime is not null);
    Assert.Throws<ObjectDisposedException>(() =>
      system.RegisteredRuntime!.Query(new GetCurrentTurnQuery()));
    return observed;
  }

  // Preparation faults happen before any runtime exists: declared systems never register.
  private static Exception AssertPreparationFailureBeforeRegistration(BattleSetup setup, SetupSystemData system)
  {
    var observed = Assert.Throws<Exception>(() =>
    {
      using var unexpected = BattleFactory.Start(setup with { Systems = [system] }).RequireRight();
    });
    Assert.True(system.RegisteredRuntime is null);
    return observed;
  }

  [TestCase(TestName = "A throwing declared-system registration preserves the exception and disposes the runtime")]
  public void ThrowingRegistrationDisposesRuntime()
  {
    var (setup, _, _) = GroupedSetup();
    var expected = new InvalidOperationException("setup sentinel");
    var system = new SetupSystemData { OnRegister = _ => throw expected };

    Assert.True(ReferenceEquals(expected, AssertStartupFailureDisposes(setup, system)));
  }

  [TestCase(typeof(SessionStartedBattleEvent), TestName = "A session-start hook fault preserves the exception and disposes the runtime")]
  [TestCase(typeof(TurnStartedBattleEvent), TestName = "A turn-start hook fault preserves the exception and disposes the runtime")]
  public void HookFaultDisposesRuntime(Type eventType)
  {
    var (setup, _, _) = GroupedSetup();
    var expected = new InvalidOperationException("setup sentinel");
    var system = new SetupSystemData
    {
      OnRegister = runtime => runtime.RegisterHook<BattleEventTag>(
        new ThrowOnSetupEvent(eventType, expected)),
    };

    Assert.True(ReferenceEquals(expected, AssertStartupFailureDisposes(setup, system)));
  }

  [TestCase(TestName = "A malformed spawn buff fault preserves its original cause and never registers declared systems")]
  public void PreparationFaultPreservesCauseAndNeverRegistersSystems()
  {
    var (setup, player, _) = GroupedSetup();
    var expected = new InvalidOperationException("preparation sentinel");
    var broken = setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Units = [new UnitPlacement(
            new UnitLoadout(TestData.MakeCombatant("Alpha", player,
              buffs: [TestData.MakeBuff("Throwing", new ThrowingBuffCondition(expected))])),
            new Vector3I(0, 0, 0))],
        },
        setup.Sides[1],
      ],
    };
    var system = new SetupSystemData();

    Exception observed = AssertPreparationFailureBeforeRegistration(broken, system);

    Assert.True(ReferenceEquals(expected, observed));
  }

  // A condition fault during preparation's spawn-buff evaluation is the preparation-fault
  // class with a capturable cause: the original exception must surface unwrapped.
  private sealed partial class ThrowingBuffCondition(Exception failure) : BuffCondition
  {
    internal override bool IsMet(BattleReadContext context, BattleUnitState unit) => throw failure;
  }

  [TestCase(TestName = "An empty side is a typed NoConsciousUnits failure naming the faction, before systems register")]
  public void EmptyRosterFailsTyped()
  {
    var (setup, _, _) = GroupedSetup();
    var empty = setup with
    {
      Sides =
      [
        setup.Sides[0],
        setup.Sides[1] with { Units = [] },
      ],
    };
    var system = new SetupSystemData();

    Either<BattleSetupFailure, BattleRuntime> started = BattleFactory.Start(empty with { Systems = [system] });

    Assert.True(started.IsLeft);
    started.IfLeft(failure =>
    {
      Assert.Equal(BattleSetupFailureReason.NoConsciousUnits, failure.Reason);
      Assert.True(failure.Message.Contains("Enemy"));
    });
    Assert.True(system.RegisteredRuntime is null);
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

  // A terminal opening-turn objective ends the battle inside the opening dispatch, yet the
  // started-turn AP refresh must still finish: the buff only activates once both sides are
  // on the board, so its +2 maximum exists exclusively in the opening turn-start pass —
  // preparation's earlier top-up saw AP 4. The end callback observes the refreshed AP.
  [TestCase(TestName = "A terminal opening-turn objective finishes buff/AP work and freezes results before the end event")]
  public void TerminalObjectiveEndsBattleDuringStartup()
  {
    var (setup, player, _) = GroupedSetup();
    var roused = TestData.MakeBuff("Roused", new RosterCompleteCondition(),
      statMods: [new ActionPointsStatMod { Modifiers = [StatModifier.Add(2)] }]);
    int liveApAtSessionEnd = -1;
    var observer = new SetupSystemData
    {
      OnRegister = runtime => runtime.BattleEventCommitted += battleEvent =>
      {
        if (battleEvent is not SessionEndedBattleEvent)
          return;
        var unit = runtime.Query(new GetUnitAtTile(
          runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome())).RequireSome();
        liveApAtSessionEnd = unit.CurrentActionPoints;
      },
    };
    var terminal = setup with
    {
      Systems = [observer],
      Sides =
      [
        setup.Sides[0] with
        {
          Units = [new UnitPlacement(
            new UnitLoadout(TestData.MakeCombatant("Alpha", player, actionPoints: 4, buffs: [roused])),
            new Vector3I(0, 0, 0))],
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

    Assert.True(runtime.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, runtime.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.True(runtime.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.Equal(6, liveApAtSessionEnd);
  }

  // The buff only activates once both sides are on the board, so its +2 maximum exists
  // exclusively in the opening turn-start pass — preparation's earlier top-up saw AP 4.
  private sealed partial class RosterCompleteCondition : BuffCondition
  {
    internal override bool IsMet(BattleReadContext context, BattleUnitState unit) =>
      context.State.AliveUnits.AsValueEnumerable().Count() >= 2;
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
      runtime.TryGetAttackTarget(new BattleEntity.Unit(target)).RequireSome())).RequireSome();
    foreach (BattleEvent battleEvent in result.EventsThatOccurred)
      if (battleEvent is UnitAttackedBattleEvent attacked)
        return attacked;
    throw new InvalidOperationException("Expected an attack event.");
  }
}
