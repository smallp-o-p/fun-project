using System;
using System.Collections.Generic;
using FunProject.Battle;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed class EnemyTurnControllerTest
{
  private static EnemyPolicySettings Demo => new(200, 25);

  [TestCase]
  public static void EnemyTurnsAreExplicitOptInAndRequireDesignatedPlayer()
  {
    using var undesignated = BattleFixture.Duel();
    Assert.Throws<InvalidOperationException>(() => undesignated.Runtime.EnableEnemyTurns());
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetActiveSideQuery()));
    battle.Runtime.EnableEnemyTurns();
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
    Assert.Throws<InvalidOperationException>(() => battle.Runtime.EnableEnemyTurns());
  }

  [TestCase(true, true)]
  [TestCase(false, true)]
  [TestCase(true, false)]
  [TestCase(false, false)]
  public static void CompletionObserversReturnBeforeSequentialEnemySubmissionsAndInitiatingResultIsRetained(
    bool publicEntry, bool explicitEndTurn)
  {
    using var battle = BattleFixture.Duel(playerControlled: true,
      enemy: new("Enemy", Weapon: TestData.MakeAmmoWeapon("Pistol")));
    battle.Runtime.EnableEnemyTurns(Demo);
    battle.RegisterHook<ActiveSideChangedBattleEvent>(new WoundActivatedEnemy(battle.EnemyUnit));
    BattleAction initiating = explicitEndTurn ? BattleAction.EndFactionTurn(battle.PlayerFaction)
      : BattleAction.PassUnit(battle.Alive(battle.PlayerUnit));
    List<BattleAction> started = [];
    List<BattleAction> completed = [];
    bool observing = false;
    bool attemptingNested = false;
    battle.Runtime.ActionStarted += action =>
    {
      if (attemptingNested) return;
      Assert.False(observing);
      if (started.Count > 0) Assert.Equal(19, battle.EnemyUnit.CurrentHealth);
      started.Add(action);
      attemptingNested = true;
      try
      {
        Assert.Throws<InvalidOperationException>(() => battle.Submit(BattleAction.EndFactionTurn(battle.Query(new GetActiveSideQuery()))));
        Assert.Throws<InvalidOperationException>(() => battle.Runtime.EnableEnemyTurns());
      }
      finally { attemptingNested = false; }
    };
    battle.Runtime.ActionCompleted += result =>
    {
      if (attemptingNested) return;
      observing = true;
      if (ReferenceEquals(result.Action, initiating)) Assert.Equal(19, battle.EnemyUnit.CurrentHealth);
      completed.Add(result.Action);
      attemptingNested = true;
      try { Assert.Throws<InvalidOperationException>(() => battle.Submit(BattleAction.EndFactionTurn(battle.Query(new GetActiveSideQuery())))); }
      finally { observing = false; attemptingNested = false; }
    };
    var returned = publicEntry ? battle.Submit(initiating) : battle.Runtime.ExecuteSingleAction(initiating);
    Assert.Equal(initiating, returned.Action);
    Assert.False(returned.EventsThatOccurred.ToArray().AsValueEnumerable().Any(evt => evt is UnitAttackedBattleEvent));
    Assert.True(started.Count > 1);
    Assert.True(started.AsValueEnumerable().SequenceEqual(completed));
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
    Assert.True(battle.Events.EventsOf<UnitAttackedBattleEvent>().Length > 0);
  }

  [TestCase]
  public static void EnablingInitiallyActiveEnemyDeliversOrdinaryCompletionsWithoutPresentationTicks()
  {
    using var battle = BattleFixture.Duel(playerControlled: true,
      enemy: new("Enemy", Weapon: TestData.MakeAmmoWeapon("Pistol")),
      configure: setup => setup with { Sides = [setup.Sides[1], setup.Sides[0]] });
    int completed = 0;
    battle.Runtime.ActionCompleted += _ => completed++;
    var player = battle.Session.PlayerFaction.RequireSome();
    Assert.False(ReferenceEquals(player, battle.Query(new GetActiveSideQuery())));
    battle.Runtime.EnableEnemyTurns(Demo);
    Assert.Equal(player, battle.Query(new GetActiveSideQuery()));
    Assert.True(completed > 0);
  }

  [TestCase]
  public static void CompletionObserverDisposalStopsDrainAndReturnsInitiatingResult()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.Runtime.EnableEnemyTurns();
    int started = 0;
    battle.Runtime.ActionStarted += _ => started++;
    battle.Runtime.ActionCompleted += _ => battle.Runtime.Dispose();
    var action = BattleAction.EndFactionTurn(battle.PlayerFaction);
    var returned = battle.Submit(action);
    Assert.Equal(action, returned.Action);
    Assert.Equal(1, started);
    Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide);
    battle.Runtime.Dispose();
  }

  [TestCase(true)]
  [TestCase(false)]
  public static void ObserverFaultStopsAutomationWhileLaterManualSubmissionsRemainOrdinary(bool enableBeforeFault)
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    if (enableBeforeFault) battle.Runtime.EnableEnemyTurns();
    Action<BattleActionExecResult> fail = _ => throw new InvalidOperationException("Observer failed");
    battle.Runtime.ActionCompleted += fail;
    Assert.Throws<InvalidOperationException>(() => battle.EndFactionTurn(battle.PlayerFaction));
    battle.Runtime.ActionCompleted -= fail;
    if (!enableBeforeFault) Assert.Throws<InvalidOperationException>(() => battle.Runtime.EnableEnemyTurns());
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void TerminalBattleEnablementDoesNotSubmitAndMidActionInterruptEndingBattleStopsDrain()
  {
    using var alreadyEnded = BattleFixture.Duel(playerControlled: true);
    alreadyEnded.ApplyDamage(alreadyEnded.EnemyUnit, 999);
    int attempts = 0;
    alreadyEnded.Runtime.ActionStarted += _ => attempts++;
    alreadyEnded.Runtime.EnableEnemyTurns();
    Assert.Equal(0, attempts);

    using var battle = BattleFixture.Duel(playerControlled: true,
      enemy: new("Enemy", Weapon: TestData.MakeAmmoWeapon("Pistol")),
      hitChanceCalculator: new AlwaysHitCalculator());
    battle.RegisterHook<UnitAttackedBattleEvent>(new KillAttacker());
    battle.Runtime.EnableEnemyTurns(Demo);
    int completed = 0;
    battle.Runtime.ActionCompleted += _ => completed++;
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(Some(BattleOutcome.Victory), battle.Query(new GetBattleOutcomeQuery()));
    Assert.Equal(2, completed);
  }

  [TestCase]
  public static void EmptyInterruptedResultReobservesAndContinuesToReloadThenPlayerControl()
  {
    var weapon = TestData.MakeAmmoWeapon("Pistol", magazine: 1);
    using var battle = BattleFixture.Duel(playerControlled: true,
      enemy: new("Enemy", ActionPoints: 2, Weapon: weapon), hitChanceCalculator: new AlwaysHitCalculator());
    bool interrupted = false;
    int empty = 0;
    battle.Runtime.ActionStarted += action =>
    {
      if (action is AttackEntity && !interrupted)
      {
        interrupted = true;
        weapon.TrySpendShot().RequireSome();
      }
    };
    battle.Runtime.ActionCompleted += result => { if (result.EventsThatOccurred.IsEmpty) empty++; };
    battle.Runtime.EnableEnemyTurns(Demo);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, empty);
    Assert.Equal(1, battle.Events.EventsOf<UnitReloadedWeaponBattleEvent>().Length);
    Assert.Equal(1, battle.Events.EventsOf<UnitAttackedBattleEvent>().Length);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase(false)]
  [TestCase(true)]
  public static void EnemyPassCompletesDespiteCapacityChangesInObservers(bool restoreCapacity)
  {
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(2, 1, 1),
      player: new("Player", Position: Vector3I.Zero), enemy: new("Enemy", Position: new(1, 0, 0)));
    var immobilize = TestData.MakeStun(duration: 2);
    int passAttempts = 0, completedPasses = 0;
    battle.Runtime.ActionStarted += action =>
    {
      if (action is PassUnit)
      {
        passAttempts++;
        Assert.Equal(1, passAttempts);
        battle.EnemyUnit.ApplyStatusEffect(immobilize);
      }
    };
    battle.Runtime.ActionCompleted += result =>
    {
      if (result.Action is PassUnit)
      {
        if (restoreCapacity) battle.EnemyUnit.RemoveStatusEffect(immobilize);
        Assert.Equal(1, result.EventsThatOccurred.ToArray().EventsOf<UnitActivationEndedBattleEvent>().Length);
        completedPasses++;
      }
    };
    battle.Runtime.EnableEnemyTurns();
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, passAttempts);
    Assert.Equal(1, completedPasses);
    Assert.Equal(!restoreCapacity, battle.EnemyUnit.IsImmobilized);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void StaleEnemyEndTurnRejectsAndStopsAutomationWithoutRetrying()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.EnemyUnit.ApplyStatusEffect(TestData.MakeStun(duration: 2));
    int attempts = 0, completed = 0;
    battle.Runtime.ActionStarted += action =>
    {
      if (action is EndFactionTurn && ReferenceEquals(battle.Query(new GetActiveSideQuery()), battle.EnemyFaction))
      {
        attempts++;
        Assert.Equal(1, attempts);
        // Mutate the side during admission so the ordinary AI end-turn action is stale.
        battle.Session.EndFactionTurn(battle.EnemyFaction);
      }
    };
    battle.Runtime.ActionCompleted += _ => completed++;
    battle.Runtime.EnableEnemyTurns();
    Assert.Throws<InvalidOperationException>(() => battle.EndFactionTurn(battle.PlayerFaction));
    Assert.Equal(1, attempts);
    Assert.Equal(1, completed);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, attempts);
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void LegitimateTurnUsesAllActionPointsBeyondSixtyFourSubmissions()
  {
    using var battle = BattleFixture.Duel(playerControlled: true,
      player: new("Armored player", Armor: TestData.MakeArmor("Armor", armor: 10000)),
      enemy: new("Enemy", ActionPoints: 100, Weapon: TestData.MakeAmmoWeapon("Pistol", magazine: 100)),
      hitChanceCalculator: new AlwaysHitCalculator());
    battle.Runtime.EnableEnemyTurns(new(cautionPercent: 0));
    List<BattleAction> completed = [];
    battle.Runtime.ActionCompleted += result => completed.Add(result.Action);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(102, completed.Count);
    Assert.Equal(100, completed.AsValueEnumerable().Count(action => action is AttackEntity));
    Assert.Equal(0, battle.EnemyUnit.CurrentActionPoints);
    Assert.True(completed[^1] is EndFactionTurn);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase(false)]
  [TestCase(true)]
  public static void EmptyInterruptedIntentDoesNotRetryForUnrelatedObserverChanges(bool changeKnownAlly)
  {
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(3, 1, 1),
      player: new("Player", Position: Vector3I.Zero),
      enemy: new("Enemy", Position: new(1, 0, 0), ActionPoints: 1, Weapon: TestData.MakeAmmoWeapon("Pistol")),
      configure: setup => setup with
      {
        Sides = [setup.Sides[0], setup.Sides[1] with
        {
          Units = [.. setup.Sides[1].Units,
            new(new(TestData.MakeCombatant("Scout", setup.Sides[1].Faction)), new(2, 0, 0))],
        }],
      });
    int attempts = 0;
    battle.Runtime.ActionStarted += action =>
    {
      if (action is AttackEntity)
      {
        attempts++;
        Assert.True(attempts <= 1); // Fail promptly if retry avoidance regresses.
        if (attempts == 1) battle.EnemyUnit.ClearVisibleUnits(); // Lose this actor's sight before its ordinary action commits.
      }
    };
    battle.Runtime.ActionCompleted += result =>
    {
      if (result.Action is AttackEntity && result.EventsThatOccurred.IsEmpty)
      {
        battle.EnemyUnit.AddVisibleUnit(battle.PlayerUnit);
        if (changeKnownAlly) Assert.True(battle.UnitAt(new(2, 0, 0)).TrySpendActionPoints(1));
      }
    };
    battle.Runtime.EnableEnemyTurns(Demo);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, attempts);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void FiniteReinforcementsFinishTheirTurnsWithoutAWholeCallCutoff()
  {
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(142, 1, 1),
      player: new("Player", Position: Vector3I.Zero), enemy: new("Enemy", Position: new(1, 0, 0)),
      configure: setup => setup with
      {
        Sides = setup.Sides.AsValueEnumerable().Select(side => side with
        {
          Units = side.Units.AsValueEnumerable().Select(unit => unit with
          {
            Loadout = new(TestData.MakeCombatant(unit.Loadout.Combatant.Name, side.Faction, vision: 0)),
          }).ToArray(),
        }).ToArray(),
      });
    battle.RegisterHook<TurnEndedBattleEvent>(new SpawnNextFaction(140));
    battle.Runtime.EnableEnemyTurns();
    int completed = 0;
    battle.Runtime.ActionCompleted += _ => completed++;
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(142, completed);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
  }

  private sealed class SpawnNextFaction(int remaining) : BattleHook<TurnEndedBattleEvent>
  {
    private int _next = 2;
    private int _remaining = remaining;
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent evt)
    {
      if (_remaining == 0) return [];
      _remaining--;
      var faction = TestData.MakeFaction($"Enemy{_next}");
      var position = context.Session.Board.ValidatePoint(new(_next++, 0, 0)).RequireSome();
      return [BattleAction.SpawnUnit(TestData.MakeCombatant("Enemy", faction, vision: 0), position)];
    }
  }

  private sealed class KillAttacker : BattleHook<UnitAttackedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAttackedBattleEvent evt)
      => [BattleAction.ApplyDamage(context.Session.TryGetAlive(evt.Unit).RequireSome(), 999)];
  }

  private sealed class WoundActivatedEnemy(BattleUnitState enemy) : BattleHook<ActiveSideChangedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, ActiveSideChangedBattleEvent evt)
      => ReferenceEquals(evt.Faction, enemy.Side) ? [BattleAction.ApplyDamage(context.Session.TryGetAlive(enemy).RequireSome(), 1)] : [];
  }
}
