using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using GdUnit4;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleFixtureTest
{
  private static BattleSetup SingleUnitSetup(Faction faction, string unitName = "Alpha")
    => new(
      TestData.MakeOpenBattleMap(new Vector3I(3, 1, 3)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(unitName, faction)), new Vector3I(0, 0, 0))])],
      Seed: 7);

  [TestCase]
  public void SetupAndActionsShareOneNativeUnitAndExplicitEventWindow()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(SingleUnitSetup(faction));
    var unit = battle.UnitAt(new Vector3I(0, 0, 0));

    Assert.True(ReferenceEquals(unit, battle.UnitAt(new Vector3I(0, 0, 0))));
    Assert.Equal(1, battle.Events.EventsOf<UnitAddedBattleEvent>().Length);
    Assert.Equal(1, battle.Events.EventsOf<SessionStartedBattleEvent>().Length);
    Assert.True(ReferenceEquals(battle.Board, battle.Session.Board));

    battle.ClearEvents();
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));
    Assert.Equal(new Vector3I(1, 0, 0), battle.Alive(unit).Position.Raw);
    Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<UnitAddedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<SessionStartedBattleEvent>().Length);
  }

  [TestCase]
  public void RepeatedMovesMintFreshProofsOnTheSameFixture()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    battle.ClearEvents();
    battle.Move(battle.Unit, [new Vector3I(1, 0, 0)]);
    battle.Move(battle.Unit, [new Vector3I(2, 0, 0)]);
    Assert.Equal(new Vector3I(2, 0, 0), battle.Alive(battle.Unit).Position.Raw);
    Assert.Equal(2, battle.Unit.CurrentActionPoints);
    Assert.Equal(2, battle.Events.EventsOf<UnitMovedBattleEvent>().Length);
  }

  [TestCase]
  public void ExplicitInvalidActionsStillPropagateTheirRejection()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var action = BattleAction.MoveUnit(battle.Alive(battle.Unit), [battle.At(2, 0, 0)]);
    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), battle.Alive(battle.Unit).Position.Raw);
  }

  // Two inert-objective sides with no designated player faction: a startup hook can wipe and
  // refill a side mid-startup without any objective directive or player-wipe backstop ending
  // the battle, so both reinforcement scenarios run to completion inside the factory call.
  private static BattleSetup TwoSideInertObjectiveSetup(
    Faction playerFaction, Faction enemyFaction, Combatant authoredEnemy,
    params BattleTypeSystemData[] startupSystems)
    => new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(8, 1, 8)),
      [
        new BattleSideSetup(playerFaction, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", playerFaction)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemyFaction, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(authoredEnemy), new Vector3I(4, 0, 4))]),
      ],
      Seed: 7) with
    { Systems = [.. startupSystems] };

  [TestCase(TestName = "A startup reinforcement cannot steal an authored fixture role")]
  public void StartupReinforcementKeepsAuthoredRoles()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    var authoredEnemy = TestData.MakeCombatant("Authored", enemyFaction, health: 30);
    var reinforcement = TestData.MakeCombatant("Interloper", enemyFaction);

    using var battle = new BattleFixture(TwoSideInertObjectiveSetup(
      playerFaction, enemyFaction, authoredEnemy,
      new SetupSystemData
      {
        OnRegister = runtime =>
        runtime.RegisterHook<UnitAddedBattleEvent>(new ReinforceOnceOnFirstUnitAdded(reinforcement, new Vector3I(7, 0, 7)))
      }));

    // The interrupt spawned after the bootstrap's complete placement primitive...
    var interloper = battle.UnitAt(new Vector3I(7, 0, 7));
    Assert.Equal("Interloper", interloper.Combatant.Name);
    // ...but the authored placement still owns the enemy role.
    Assert.True(ReferenceEquals(battle.EnemyUnit.Combatant, authoredEnemy));
    Assert.False(ReferenceEquals(battle.EnemyUnit, interloper));
    Assert.Equal("Alpha", battle.PlayerUnit.Combatant.Name);
  }

  [TestCase(TestName = "A startup replacement reusing the authored combatant and cell cannot steal the authored raw unit")]
  public void StartupReplacementRetainsAuthoredRawUnit()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    var authoredEnemy = TestData.MakeCombatant("Authored", enemyFaction, health: 30);
    var authoredCell = new Vector3I(4, 0, 4);

    using var battle = new BattleFixture(TwoSideInertObjectiveSetup(
      playerFaction, enemyFaction, authoredEnemy,
      new SetupSystemData
      {
        OnRegister = runtime =>
        runtime.RegisterHook<TurnStartedBattleEvent>(new ReplaceAuthoredEnemyOnFirstTurnStart(authoredEnemy, authoredCell))
      }));

    BattleUnitState[] adds = battle.Events.EventsOf<UnitAddedBattleEvent>()
      .AsValueEnumerable()
      .Where(added => ReferenceEquals(added.Unit.Combatant, authoredEnemy) && added.Position.Raw == authoredCell)
      .Select(added => added.Unit)
      .ToArray();

    // The replacement actually committed on the authored lookup key and the battle survived
    // the mid-startup wipe, so the role contract below is exercised, not vacuous.
    Assert.Equal(2, adds.Length);
    Assert.True(battle.Session.Outcome.IsNone);
    // The authored role stays the originally recorded unit: dead but retained...
    Assert.True(ReferenceEquals(battle.EnemyUnit, adds[0]));
    Assert.True(battle.EnemyUnit.IsDead);
    // ...while the replacement occupies the cell, alive and distinct.
    BattleUnitState replacement = battle.UnitAt(authoredCell);
    Assert.True(replacement.IsAlive);
    Assert.False(ReferenceEquals(battle.EnemyUnit, replacement));
  }

  private sealed class ReinforceOnceOnFirstUnitAdded(Combatant reinforcement, Vector3I where)
    : BattleHook<UnitAddedBattleEvent>
  {
    private bool _fired;

    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAddedBattleEvent evt)
    {
      if (_fired)
        return [];
      _fired = true;
      return [BattleAction.SpawnUnit(reinforcement, context.Session.Board.At(where))];
    }
  }

  // Kills the authored enemy at the first turn start and respawns its exact combatant at the
  // same cell, so the replacement's unit-added record reuses the authored role's lookup key.
  private sealed class ReplaceAuthoredEnemyOnFirstTurnStart(Combatant authored, Vector3I cell)
    : BattleHook<TurnStartedBattleEvent>
  {
    private bool _fired;

    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnStartedBattleEvent evt)
    {
      if (_fired)
        return [];
      _fired = true;
      BattleUnitState target = context.Session.AliveUnits.AsValueEnumerable()
        .Single(unit => ReferenceEquals(unit.Combatant, authored));
      return
      [
        BattleAction.ApplyDamage(context.Session.TryGetAlive(target).RequireSome(), 999),
        BattleAction.SpawnUnit(authored, context.Session.Board.At(cell)),
      ];
    }
  }

  [TestCase]
  public void PresentationObjectsAreReusedAndOwnedNodesAreFreed()
  {
    using var uiBattle = BattleFixture.UiBattle();
    Assert.True(ReferenceEquals(uiBattle.Ui, uiBattle.Ui));
    using var battle = new BattleFixture(SingleUnitSetup(TestData.MakeFaction("Player")));
    var mesh = battle.OwnNode(new Godot.Node3D());
    var director = battle.AttachDirector(new EventPlaybackDirector());
    Assert.True(ReferenceEquals(director, battle.AttachDirector(director)));
    BattleActionExecResult spawn = battle.Submit(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Extra", battle.PlayerFaction), battle.At(0, 0, 1)));
    // Tick exactly once per committed event: a second Bind subscription would leave the
    // duplicated copies queued after this loop and fail the idle assertion below.
    int committed = spawn.EventsThatOccurred.Length;
    Assert.True(committed > 0);
    for (int step = 0; step < committed; step++)
    {
      Assert.True(director.Busy);
      director.Tick();
    }
    Assert.False(director.Busy);
    battle.Dispose();
    battle.Dispose();
    Assert.False(Godot.GodotObject.IsInstanceValid(mesh));
    Assert.False(Godot.GodotObject.IsInstanceValid(director));
    uiBattle.Dispose();
    Assert.Throws<ObjectDisposedException>(() => { _ = uiBattle.Ui; });
  }

  [TestCase]
  public void DisposalDetachesRecordingAndClosesTheRuntime()
  {
    var faction = TestData.MakeFaction("Player");
    var battle = new BattleFixture(SingleUnitSetup(faction));
    int recorded = battle.Events.Count;

    battle.Dispose();
    battle.Dispose();

    using var replacement = new BattleActionExecutor(battle.Session);
    replacement.Submit(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Later", faction), battle.Board.At(2, 0, 2)));
    Assert.Equal(recorded, battle.Events.Count);
    Assert.Throws<ObjectDisposedException>(() => battle.Query(new GetBattleOutcomeQuery()));
    Assert.Throws<ObjectDisposedException>(() => battle.ClearEvents());
  }
}
