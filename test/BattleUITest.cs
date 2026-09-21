using FunProject.Battle;
using Godot;
using GdUnit4;
using System.Collections.Generic;
using System.Threading.Tasks;
using static FunProject.Tests.GeoscapeTestScenes;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Exercises the authored scenes/battle/BattleUI.tscn as a pure view: view models are pushed in
// with no runtime or FSM present, widgets and overlays must react, and intents raised by the
// nested HUD must re-raise through the BattleUI root.
[TestSuite]
[RequireGodotRuntime]
public partial class BattleUITest
{
  private static BattleUI BuildView()
    => AddToTree(GD.Load<PackedScene>("res://scenes/battle/BattleUI.tscn").Instantiate<BattleUI>());

  [TestCase(TestName = "Unit readouts and verb options render from pushed view models")]
  public void PushedViewModelsRender()
  {
    using var battle = BattleFixture.Duel();
    var units = battle.Query(new GetFactionAliveUnits(battle.PlayerUnit.Side))
      .AsValueEnumerable().Select(unit => unit.State).ToArray();
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    var moveOption = new MoveActionOption(
      actions.AsValueEnumerable().First(action => action.Action is MoveActionDefinition));
    var passOption = new PassActionOption(
      actions.AsValueEnumerable().First(action => action.Action is PassActionDefinition));
    var view = BuildView();

    view.ShowUnits(units);
    view.ShowActionOptions([moveOption, passOption]);

    var status = view.GetNode<Label>("BattleHud/VBox/UnitStatus");
    Assert.True(status.Text.Contains("HP"));
    Assert.Equal(units.Length, status.Text.Split("\n").Length);
    var verbs = view.GetNode<VBoxContainer>("BattleHud/VBox/VerbButtons");
    Assert.Equal(2, verbs.GetChildCount());
    Assert.Equal("Move", ((Button)verbs.GetChild(0)).Text);
  }

  [TestCase(TestName = "Targeting shows candidate markers and clears them")]
  public async Task TargetingOverlaysShowAndClear()
  {
    var view = BuildView();
    var highlighter = view.GetNode<ReachableTileHighlighter>("ReachableTileHighlighter");

    view.ShowTargeting([new Vector3I(0, 0, 0), new Vector3I(1, 0, 0), new Vector3I(2, 0, 0)], None);

    Assert.Equal(3, highlighter.GetChildCount());

    view.HideTargeting();
    await WaitForDeferredDeletion(((SceneTree)Engine.GetMainLoop()));

    Assert.Equal(0, highlighter.GetChildCount());
    Assert.False(view.GetNode<MeshInstance3D>("MovementLine").Visible);
    Assert.False(view.GetNode<Label>("BattleHud/VBox/HitChance").Visible);
  }

  [TestCase(TestName = "Previews route by kind: path draws the line, attack shows hit chance")]
  public void PreviewRendersByKind()
  {
    var view = BuildView();

    view.ShowPreview(new PathPreview([new Vector3I(0, 0, 0)]));
    Assert.False(view.GetNode<MeshInstance3D>("MovementLine").Visible);

    view.ShowPreview(new PathPreview([new Vector3I(0, 0, 0), new Vector3I(2, 0, 0), new Vector3I(4, 0, 0)]));
    Assert.True(view.GetNode<MeshInstance3D>("MovementLine").Visible);
    Assert.False(view.GetNode<Label>("BattleHud/VBox/HitChance").Visible);

    view.ShowPreview(new AttackPreview(new HitChanceBreakdown(65, [])));
    Assert.False(view.GetNode<MeshInstance3D>("MovementLine").Visible);
    var hitChance = view.GetNode<Label>("BattleHud/VBox/HitChance");
    Assert.True(hitChance.Visible);
    Assert.Equal("65%", hitChance.Text);
  }

  [TestCase(TestName = "HUD intents re-raise through the BattleUI root")]
  public void HudIntentsRaiseThroughRoot()
  {
    using var battle = BattleFixture.Duel();
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    var moveOption = new MoveActionOption(
      actions.AsValueEnumerable().First(action => action.Action is MoveActionDefinition));
    var view = BuildView();
    view.ShowActionOptions([moveOption]);
    int confirms = 0;
    int cancels = 0;
    UnitActionOption? selected = null;
    view.ConfirmRequested += () => confirms++;
    view.CancelRequested += () => cancels++;
    view.VerbSelected += option => selected = option;

    view.GetNode<Button>("BattleHud/VBox/ConfirmButton").EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("BattleHud/VBox/CancelButton").EmitSignal(Button.SignalName.Pressed);
    ((Button)view.GetNode<VBoxContainer>("BattleHud/VBox/VerbButtons").GetChild(0))
      .EmitSignal(Button.SignalName.Pressed);
    view.ShowBattleOver("VICTORY");

    Assert.Equal(1, confirms);
    Assert.Equal(1, cancels);
    Assert.True(ReferenceEquals(moveOption, selected));
    var banner = view.GetNode<Label>("BattleHud/BattleOverBanner");
    Assert.True(banner.Visible);
    Assert.Equal("VICTORY", banner.Text);
  }
}
