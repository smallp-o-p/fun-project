using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items.Effects;
using FunProject.Stats;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class BattleStartupLifecycleTest
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
  public static void DeclaredSystemObservesFirstTurn()
  {
    var type = TestData.MakeDuelBattleType();
    var hook = new RecordingHook();
    type.Systems.Add(new SetupSystemData
    {
      OnRegister = runtime => runtime.RegisterHook<IBattleEventTag>(hook),
    });
    using var runtime = BattleFactory.Start(type, seed: 7).RequireRight();
    Assert.Equal(1, hook.Received.EventsOf<TurnStartedBattleEvent>().Length);
  }

  [TestCase(TestName = "Declared systems observe spawns, object placement, session start, and the first turn")]
  public static void DeclaredSystemObservesStartupEvents()
  {
    var (setup, _, _) = GroupedSetup();
    var hook = new RecordingHook();
    var observations = new FirstUnitAddedObservations();
    var system = new SetupSystemData
    {
      OnRegister = hooks =>
      {
        hooks.RegisterHook<IBattleEventTag>(hook);
        hooks.RegisterHook<UnitAddedBattleEvent>(observations);
      },
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

    // Startup hooks observe the COMPLETE initial board with valid turn state: the second
    // unit and the placed object already exist when the first unit-added notification
    // fires, and the active side's availability is established.
    Assert.True(observations.Observed);
    Assert.Equal(2, observations.AliveUnitCount);
    Assert.Equal(1, observations.ObjectCount);
    Assert.True(ReferenceEquals(setup.Sides[0].Faction, observations.ActiveSide));
    Assert.True(observations.FirstUnitAvailable);
    Assert.True(observations.FirstUnitCanAct);
  }

  [TestCase(TestName = "Initial spottings follow every unit announcement in deterministic setup order")]
  public static void InitialSpottingsFollowUnitAnnouncements()
  {
    var (setup, _, _) = GroupedSetup();
    var hook = new RecordingHook();
    var observations = new FirstUnitAddedObservations();
    var system = new SetupSystemData
    {
      OnRegister = hooks =>
      {
        hooks.RegisterHook<IBattleEventTag>(hook);
        hooks.RegisterHook<UnitAddedBattleEvent>(observations);
      },
    };
    using var runtime = BattleFactory.Start(setup with { Systems = [system] }).RequireRight();

    var added = hook.Received.EventsOf<UnitAddedBattleEvent>();
    Assert.Equal(2, added.Length);
    var spotted = hook.Received.EventsOf<UnitSpottedBattleEvent>();
    Assert.Equal(2, spotted.Length);
    // Exactly one spotting per directed pair; ids follow setup order (0,1) then (1,0).
    Assert.True(ReferenceEquals(added[0].Unit, spotted[0].Unit));
    Assert.True(ReferenceEquals(added[1].Unit, spotted[0].Target));
    Assert.True(ReferenceEquals(added[1].Unit, spotted[1].Unit));
    Assert.True(ReferenceEquals(added[0].Unit, spotted[1].Target));
    Assert.Equal(added[0].Unit.Id, spotted[0].Unit.Id);
    Assert.Equal(added[1].Unit.Id, spotted[0].Target.Id);
    Assert.Equal(added[1].Unit.Id, spotted[1].Unit.Id);
    Assert.Equal(added[0].Unit.Id, spotted[1].Target.Id);
    // Each spotting's observer and target were announced first.
    for (int i = 0; i < spotted.Length; i++)
    {
      int spottedIndex = hook.Received.IndexOf(spotted[i]);
      Assert.True(hook.Received.IndexOf(added[0]) < spottedIndex);
      Assert.True(hook.Received.IndexOf(added[1]) < spottedIndex);
    }

    // Full visibility is already computed at the FIRST placement notification.
    Assert.True(observations.Observed);
    Assert.True(observations.MutuallyVisible);
  }

  [TestCase(false, TestName = "Without completion a startup interrupt executes once after the first turn-start event")]
  [TestCase(true, TestName = "With completion during placement, notifications finish but the queued interrupt is dropped")]
  public static void StartupInterruptsRunOnlyAfterBootstrap(bool endDuringPlacement)
  {
    var (setup, _, _) = GroupedSetup();
    var interrupt = new DamageFirstAddedUnitOnce();
    var hook = new RecordingHook();
    var ready = setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" }, new Vector3I(1, 0, 0))],
      Sides =
      [
        setup.Sides[0] with
        {
          Objectives = endDuringPlacement
            ?
            [
              new FakeObjectiveData
              {
                Complete = true,
                Observe = typeof(UnitAddedBattleEvent),
                OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
              },
            ]
            : [new FakeObjectiveData()],
        },
        setup.Sides[1],
      ],
      Systems =
      [
        new SetupSystemData { OnRegister = runtime => runtime.RegisterHook<IBattleEventTag>(hook) },
        new SetupSystemData { OnRegister = runtime => runtime.RegisterHook<UnitAddedBattleEvent>(interrupt) },
      ],
    };
    using var runtime = BattleFactory.Start(ready).RequireRight();

    Assert.True(interrupt.HadSourceAction);
    var added = hook.Received.EventsOf<UnitAddedBattleEvent>();
    Assert.Equal(2, added.Length);
    Assert.Equal(1, hook.Received.EventsOf<ObjectPlacedBattleEvent>().Length);

    if (!endDuringPlacement)
    {
      Assert.True(runtime.Query(new GetBattleOutcomeQuery()).IsNone);
      var damaged = hook.Received.EventsOf<UnitDamagedBattleEvent>();
      Assert.Equal(1, damaged.Length);
      hook.Received.EventBefore<TurnStartedBattleEvent, UnitDamagedBattleEvent>();
      // The interrupt waited for the whole startup primitive: even the LAST unit-added
      // notification precedes the damage.
      int damageIndex = hook.Received.IndexOf(damaged[0]);
      Assert.True(hook.Received.IndexOf(added[1]) < damageIndex);
      Assert.Equal(added[0].Unit.MaxHealth - 1, added[0].Unit.CurrentHealth);
    }
    else
    {
      Assert.Equal(BattleOutcome.Victory, runtime.Query(new GetBattleOutcomeQuery()).RequireSome());
      Assert.Equal(0, hook.Received.EventsOf<SessionStartedBattleEvent>().Length);
      Assert.Equal(0, hook.Received.EventsOf<TurnStartedBattleEvent>().Length);
      Assert.Equal(added[0].Unit.MaxHealth, added[0].Unit.CurrentHealth);
      Assert.Equal(1, hook.Received.EventsOf<SessionEndedBattleEvent>().Length);
    }
  }

  // Captures what the first unit-added startup hook can observe: the complete roster, the
  // placed object, valid turn state, established active-side availability, and mutual
  // visibility computed from the complete board.
  private sealed class FirstUnitAddedObservations : BattleHook<UnitAddedBattleEvent>
  {
    public bool Observed { get; private set; }
    public int AliveUnitCount { get; private set; }
    public int ObjectCount { get; private set; }
    public Faction? ActiveSide { get; private set; }
    public bool FirstUnitAvailable { get; private set; }
    public bool FirstUnitCanAct { get; private set; }
    public bool MutuallyVisible { get; private set; }

    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAddedBattleEvent evt)
    {
      if (Observed)
        return [];
      Observed = true;
      AliveUnitCount = context.Session.AliveUnits.AsValueEnumerable().Count();
      ObjectCount = context.Session.Objects.AsValueEnumerable().Count();
      ActiveSide = context.Session.ActiveSide;
      FirstUnitAvailable = context.Session.IsUnitStillAvailableThisTurn(evt.Unit);
      FirstUnitCanAct = context.Session.CanUnitActNow(evt.Unit);
      BattleUnitState other = context.Session.AliveUnits.AsValueEnumerable()
        .Single(unit => !ReferenceEquals(unit, evt.Unit));
      MutuallyVisible = evt.Unit.VisibleUnits.Contains(other) && other.VisibleUnits.Contains(evt.Unit);
      return [];
    }
  }

  // One-shot first-unit-added interrupt source: mints a fresh proof at hook time and
  // returns a damage action against that unit.
  private sealed class DamageFirstAddedUnitOnce : BattleHook<UnitAddedBattleEvent>
  {
    private bool _fired;

    public bool HadSourceAction { get; private set; }

    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAddedBattleEvent evt)
    {
      if (_fired)
        return [];
      _fired = true;
      HadSourceAction = context.SourceAction.IsSome;
      return [BattleAction.ApplyDamage(context.Session.TryGetAlive(evt.Unit).RequireSome(), 1)];
    }
  }

  // Spends one action point of the turn-starting side's first unit during the turn-start
  // dispatch, before the objective router can end the battle in the same dispatch.
  private sealed class SpendActiveSideApOnTurnStart : BattleHook<TurnStartedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnStartedBattleEvent evt)
    {
      BattleUnitState unit = context.Session.GetFactionAliveUnits(evt.Faction).AsValueEnumerable().First();
      Assert.True(unit.TrySpendActionPoints(1));
      return [];
    }
  }

  // Starts the real factory inside a using so even a successful Start disposes its runtime.
  // A capture must exist (a successful Start fails this helper), the system must have received
  // its registration capability, and registering through it after disposal must throw.
  private static Exception AssertStartupFailureDisposes(BattleSetup setup, SetupSystemData system)
  {
    Exception? observed = null;
    try
    {
      using var unexpected = BattleFactory.Start(setup with { Systems = [system] }).RequireRight();
    }
    catch (Exception error)
    {
      observed = error;
    }
    Assert.True(observed is not null);
    // The declared system registered before the fault; the retained capability died with the
    // runtime — registering through it throws, and no runtime getter is added for tests.
    Assert.True(system.RegisteredHooks is not null);
    Assert.Throws<ObjectDisposedException>(() =>
      system.RegisteredHooks!.RegisterHook<IBattleEventTag>(new RecordingHook()));
    return observed!;
  }

  [TestCase(TestName = "A throwing declared-system registration preserves the exception and disposes the runtime")]
  public static void ThrowingRegistrationDisposesRuntime()
  {
    var (setup, _, _) = GroupedSetup();
    var expected = new InvalidOperationException("setup sentinel");
    var system = new SetupSystemData { OnRegister = _ => throw expected };

    Assert.True(ReferenceEquals(expected, AssertStartupFailureDisposes(setup, system)));
  }

  [TestCase(typeof(UnitAddedBattleEvent), TestName = "A unit-added hook fault preserves the exception and disposes the runtime")]
  [TestCase(typeof(SessionStartedBattleEvent), TestName = "A session-start hook fault preserves the exception and disposes the runtime")]
  public static void HookFaultDisposesRuntime(Type eventType)
  {
    var (setup, _, _) = GroupedSetup();
    var expected = new InvalidOperationException("setup sentinel");
    var system = new SetupSystemData
    {
      OnRegister = hooks => hooks.RegisterHook<IBattleEventTag>(
        new ThrowOnSetupEvent(eventType, expected)),
    };

    Assert.True(ReferenceEquals(expected, AssertStartupFailureDisposes(setup, system)));
  }

  [TestCase(TestName = "A malformed spawn buff fails unit materialization before any registration")]
  public static void MalformedSpawnBuffFailsBeforeRegistration()
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

    // Unit materialization runs before the session exists, so nothing registers and no
    // runtime is created to dispose; the original cause propagates.
    Exception? observed = null;
    try
    {
      BattleFactory.Start(broken with { Systems = [system] });
    }
    catch (Exception error)
    {
      observed = error;
    }
    Assert.True(observed is InvalidOperationException);
    Assert.True(observed!.Message.Contains("no activation condition", StringComparison.Ordinal));
    Assert.True(system.RegisteredHooks is null);
  }

  [TestCase(TestName = "A battle with objectives but no units fails construction before any registration")]
  public static void EmptyRostersFailConstructionWithoutRegistration()
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

    // The roster cannot start: the scheduler's construction invariant fires before the
    // session/runtime exist, so nothing registers and there is nothing to dispose.
    Assert.Throws<InvalidOperationException>(() =>
      BattleFactory.Start(empty with { Systems = [system] }));
    Assert.True(system.RegisteredHooks is null);
  }

  [TestCase(TestName = "Repeated starts from one setup get fresh board, objective, object, and hook state")]
  public static void IndependentStartsDoNotShareMutableState()
  {
    var (setup, _, _) = GroupedSetup();
    var hooks = new List<RecordingHook>();
    var system = new SetupSystemData
    {
      OnRegister = runtime =>
      {
        var hook = new RecordingHook();
        hooks.Add(hook);
        runtime.RegisterHook<IBattleEventTag>(hook);
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
  public static void TerminalObjectiveEndsBattleDuringStartup()
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
    using var runtime = BattleFactory.Start(terminal with
    {
      Systems = [new SetupSystemData
      {
        OnRegister = hooks => hooks.RegisterHook<TurnStartedBattleEvent>(new SpendActiveSideApOnTurnStart()),
      }],
    }).RequireRight();
    Assert.True(runtime.Query(new GetBattleOutcomeQuery()).IsSome);
    Assert.Equal(BattleOutcome.Victory, runtime.Query(new GetBattleResultQuery()).RequireRight().Outcome);

    // The observer's spend happened during the terminal turn-start dispatch and no refresh
    // followed it: the battle ended before any AP restoration could run.
    var playerUnit = runtime.Query(new GetFactionAliveUnits(setup.Sides[0].Faction))
      .AsValueEnumerable().Single().State;
    Assert.Equal(playerUnit.MaxActionPoints - 1, playerUnit.CurrentActionPoints);
  }

  [TestCase(TestName = "The startup AP refresh reads buffs activated during placement")]
  public static void StartupApRefreshReadsSpawnActivatedBuffs()
  {
    var apBuff = TestData.MakeBuff(
      "Adrenaline",
      new AdjacentEnemyCondition(),
      statMods: [new ActionPointsStatMod { Modifiers = [StatModifier.Add(2)] }]);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", ActionPoints: 4, Position: new Vector3I(4, 0, 1), Buffs: [apBuff]),
      enemy: new("Hostile", Position: new Vector3I(4, 0, 2)));

    // Placement activated the adjacency buff (spawn evaluation); the first-turn refresh
    // read the buffed maximum — the ordering BuffBattleTest pins for later turns.
    Assert.Equal(6, battle.PlayerUnit.CurrentActionPoints);
  }

  [TestCase(TestName = "Seed 7 produces the same combat rolls from the type and resolved setups")]
  public static void SeedDrivesIdenticalCombatRolls()
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
  public static void HitCalculatorInjectionReachesCombat()
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
