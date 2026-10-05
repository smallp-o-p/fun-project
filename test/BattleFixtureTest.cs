using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items.Capabilities;
using GdUnit4;
using System;

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
  public void OneOwningTurnEndTicksRegenExactlyOnce()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal,
      regenDelayTurns: 2, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(player: new("Alpha", Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 5);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, armor.Capability.Current);
  }

  [TestCase]
  public void ExplicitInvalidActionsStillPropagateTheirRejection()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var action = BattleAction.MoveUnit(battle.Alive(battle.Unit), [battle.At(2, 0, 0)]);
    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), battle.Alive(battle.Unit).Position.Raw);
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
    battle.Spawn(TestData.MakeCombatant("Extra", battle.PlayerFaction), new Vector3I(0, 0, 1));
    Assert.True(director.Busy); // one subscription queued the spawn's events
    while (director.Busy)
      director.Tick();
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
    Assert.Throws<ObjectDisposedException>(() => battle.Query(new GetBattlePhaseQuery()));
    Assert.Throws<ObjectDisposedException>(() => battle.ClearEvents());
  }
}
