using FunProject.Battle;
using GdUnit4;
using System;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleFixtureTest
{
  [TestCase]
  public void SetupAndActionsShareOneNativeUnitAndExplicitEventWindow()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    Assert.True(ReferenceEquals(unit, battle.UnitAt(new Vector3I(0, 0, 0))));
    Assert.Equal(1, battle.Events.EventsOf<UnitAddedBattleEvent>().Length);
    battle.Start();
    Assert.Equal(1, battle.Events.EventsOf<UnitAddedBattleEvent>().Length);
    battle.ClearEvents();
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));
    Assert.Equal(new Vector3I(1, 0, 0), battle.Alive(unit).Position.Raw);
    Assert.Equal(1, battle.Events.EventsOf<UnitMovedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<UnitAddedBattleEvent>().Length);
  }

  [TestCase]
  public void DisposalDetachesRecordingAndClosesTheRuntime()
  {
    var faction = TestData.MakeFaction("Player");
    var battle = new BattleFixture(new Vector3I(3, 1, 3), [faction]);
    battle.Dispose();
    battle.Dispose();
    using var replacement = new BattleActionExecutor(battle.Session);
    replacement.Submit(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Later", faction), battle.Board.At(0, 0, 0)));
    Assert.Equal(0, battle.Events.Count);
    Assert.Throws<ObjectDisposedException>(() => battle.Query(new GetBattlePhaseQuery()));
    Assert.Throws<ObjectDisposedException>(() => battle.ClearEvents());
  }
}
