using FunProject.Battle;
using Godot;
using GdUnit4;
using System.Collections.Generic;
using static FunProject.Tests.GeoscapeTestScenes;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Exercises the authored scenes/battle/BattleHud.tscn: tree entry stands in for scene load,
// so _Ready's %-wiring and the authored defaults are what these cases pin down.
[TestSuite]
[RequireGodotRuntime]
public partial class BattleHudTest
{
  private static BattleHud BuildHud()
    => AddToTree(GD.Load<PackedScene>("res://scenes/battle/BattleHud.tscn").Instantiate<BattleHud>());

  [TestCase(TestName = "Confirm and Cancel buttons raise the HUD intent events")]
  public void ConfirmAndCancelButtonsRaiseIntentEvents()
  {
    var hud = BuildHud();
    int confirms = 0;
    int cancels = 0;
    hud.ConfirmRequested += () => confirms++;
    hud.CancelRequested += () => cancels++;

    hud.GetNode<Button>("%ConfirmButton").EmitSignal(Button.SignalName.Pressed);
    hud.GetNode<Button>("%CancelButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(1, confirms);
    Assert.Equal(1, cancels);
  }

  [TestCase(TestName = "Rebuilt verb buttons carry their labels and raise VerbSelected")]
  public void VerbButtonsRaiseVerbSelected()
  {
    using var battle = BattleFixture.Duel();
    IReadOnlyList<UnitAction> actions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    UnitAction moveAction = actions.AsValueEnumerable().First(action => action.Action is MoveActionDefinition);
    UnitAction passAction = actions.AsValueEnumerable().First(action => action.Action is PassActionDefinition);
    var moveOption = new MoveActionOption(moveAction);
    var hud = BuildHud();

    hud.ShowActionOptions([moveOption, new PassActionOption(passAction)]);

    var verbs = hud.GetNode<Container>("%VerbButtons");
    Assert.Equal(2, verbs.GetChildCount());
    var moveButton = (Button)verbs.GetChild(0);
    Assert.Equal("Move", moveButton.Text);
    UnitActionOption? selected = null;
    hud.VerbSelected += option => selected = option;

    moveButton.EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(moveOption, selected));
  }

  [TestCase(TestName = "Battle over banner starts hidden and shows the pushed text")]
  public void BattleOverBannerShowsPushedText()
  {
    var hud = BuildHud();
    Label banner = hud.GetNode<Label>("%BattleOverBanner");
    Assert.False(banner.Visible);

    hud.ShowBattleOver("VICTORY");

    Assert.True(banner.Visible);
    Assert.Equal("VICTORY", banner.Text);
  }

  [TestCase(TestName = "Hit chance label starts hidden, shows the pushed chance, hides again")]
  public void HitChanceLabelShowsAndHides()
  {
    var hud = BuildHud();
    Label hitChance = hud.GetNode<Label>("%HitChance");
    Assert.False(hitChance.Visible);

    hud.ShowHitChance(73);
    Assert.True(hitChance.Visible);
    Assert.Equal("73%", hitChance.Text);

    hud.HideHitChance();
    Assert.False(hitChance.Visible);
  }
}
