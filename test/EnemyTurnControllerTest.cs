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

  [TestCase]
  public static void CompletionObserversReturnBeforeSequentialEnemySubmissionsAndInitiatingResultIsRetained()
  {
    using var battle = BattleFixture.Duel(playerControlled: true,
      enemy: new("Enemy", Weapon: TestData.MakeAmmoWeapon("Pistol")));
    battle.Runtime.EnableEnemyTurns(Demo);
    var initiating = BattleAction.EndFactionTurn(battle.PlayerFaction);
    List<BattleAction> started = [];
    List<BattleAction> completed = [];
    bool observing = false;
    bool attemptingNested = false;
    battle.Runtime.ActionStarted += action =>
    {
      if (attemptingNested) return;
      Assert.False(observing);
      started.Add(action);
      attemptingNested = true;
      try { Assert.Throws<InvalidOperationException>(() => battle.Submit(BattleAction.EndFactionTurn(battle.Query(new GetActiveSideQuery())))); }
      finally { attemptingNested = false; }
    };
    battle.Runtime.ActionCompleted += result =>
    {
      if (attemptingNested) return;
      observing = true;
      completed.Add(result.Action);
      attemptingNested = true;
      try { Assert.Throws<InvalidOperationException>(() => battle.Submit(BattleAction.EndFactionTurn(battle.Query(new GetActiveSideQuery())))); }
      finally { observing = false; attemptingNested = false; }
    };
    var returned = battle.Submit(initiating);
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
    Assert.True(battle.Runtime.LatestEnemyDecisions.Count > 0);
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

  [TestCase]
  public static void SixtyFourthSubmissionEndsFactionAndLatestDiagnosticsStayBounded()
  {
    using var battle = BattleFixture.Duel(playerControlled: true,
      player: new("Armored player", Armor: TestData.MakeArmor("Armor", armor: 10000)),
      enemy: new("Enemy", ActionPoints: 100, Weapon: TestData.MakeAmmoWeapon("Pistol", magazine: 100)),
      hitChanceCalculator: new AlwaysHitCalculator());
    battle.Runtime.EnableEnemyTurns(new(cautionPercent: 0));
    List<BattleAction> completed = [];
    battle.Runtime.ActionCompleted += result => completed.Add(result.Action);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(65, completed.Count);
    Assert.Equal(63, completed.AsValueEnumerable().Count(action => action is AttackEntity));
    Assert.True(completed[^1] is EndFactionTurn);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
    Assert.Equal(64, battle.Runtime.LatestEnemyDecisions.Count);
  }

  [TestCase]
  public static void EmptyNoProgressIntentIsNotRetriedAndCannotLoop()
  {
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(3, 1, 1),
      player: new("Player", Position: Vector3I.Zero),
      enemy: new("Enemy", Position: new(1, 0, 0), Weapon: TestData.MakeAmmoWeapon("Pistol")),
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
        battle.EnemyUnit.ClearVisibleUnits(); // Lose this actor's sight before its ordinary action commits.
      }
    };
    battle.Runtime.EnableEnemyTurns(Demo);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, attempts);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void WholeDrainIsBoundedByInitialFactionCountWhenHooksKeepAddingFactions()
  {
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(132, 1, 1),
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
    battle.RegisterHook<TurnEndedBattleEvent>(new SpawnNextFaction());
    battle.Runtime.EnableEnemyTurns();
    int completed = 0;
    battle.Runtime.ActionCompleted += _ => completed++;
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(129, completed);
    Assert.Equal(64, battle.Runtime.LatestEnemyDecisions.Count);
  }

  private sealed class SpawnNextFaction : BattleHook<TurnEndedBattleEvent>
  {
    private int _next = 2;
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent evt)
    {
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
}
