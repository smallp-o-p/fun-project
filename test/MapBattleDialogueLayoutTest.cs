using System.Threading.Tasks;
using FunProject.Battle;
using FunProject.Dialogue;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MapBattleDialogueLayoutTest
{
  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task GeoscapeKeepsNavigationAtBottomAndAlertsScrollable(int width, int height)
  {
    var hud = CreateHud();
    var viewport = CreateUiViewport(hud, new Vector2I(width, height));
    SysColGeneric.List<FunProject.Strategic.GeoscapeEvent> events = [];
    for (int i = 0; i < 24; i++)
      events.Add(new(TestData.MakeEvent($"A long field report from the northern frontier {i}"), None, 0, None));
    hud.RefreshAlerts(events);
    hud.UpdateCountdowns(0);
    await WaitForLayout(viewport);

    foreach (string name in new[] { "UnitsButton", "EngineeringButton", "CaptivityButton", "PauseButton", "SpeedButton" })
    {
      var button = hud.GetNode<Button>($"%{name}");
      AssertInside(button, width, height);
      Assert.True(ScreenRect(button).Position.Y > height * 0.65f);
      Assert.Equal(Control.FocusModeEnum.All, button.FocusMode);
    }
    var scroll = hud.GetNode<ScrollContainer>("%AlertScroll");
    AssertInside(scroll, width, height);
    Assert.True(scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page);
    var last = hud.GetNode<VBoxContainer>("%Alerts").GetChild<Button>(23);
    last.GrabFocus();
    await WaitForLayout(viewport);
    Assert.True(ScreenRect(scroll).Intersects(ScreenRect(last)));
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task TacticalActionsStayAtBottomAndLeaveBattlefieldOpen(int width, int height)
  {
    var shell = new Control();
    var hud = GD.Load<PackedScene>("res://scenes/battle/BattleHud.tscn").Instantiate<BattleHud>();
    shell.AddChild(hud);
    var viewport = CreateUiViewport(shell, new Vector2I(width, height));
    using var battle = BattleFixture.Duel();
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    hud.ShowActionOptions([
      new MoveActionOption(actions.AsValueEnumerable().First(action => action.Action is MoveActionDefinition)),
      new PassActionOption(actions.AsValueEnumerable().First(action => action.Action is PassActionDefinition))]);
    hud.ShowUnits([battle.PlayerUnit]);
    await WaitForLayout(viewport);

    var verbs = hud.GetNode<Container>("%VerbButtons");
    AssertInside(verbs, width, height);
    Assert.True(ScreenRect(verbs).Position.Y > height * 0.65f);
    Assert.True(ScreenRect(verbs.GetChild<Button>(1)).Position.X > ScreenRect(verbs.GetChild<Button>(0)).Position.X);
    AssertInside(hud.GetNode<Control>("%UnitScroll"), width, height);
    AssertInside(hud.GetNode<Button>("%ConfirmButton"), width, height);
  }

  [TestCase]
  public void EndTurnHasASeparateCommandAndPreservesTheExistingIntent()
  {
    var hud = AddToTree(GD.Load<PackedScene>("res://scenes/battle/BattleHud.tscn").Instantiate<BattleHud>());
    using var battle = BattleFixture.Duel();
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    var option = new EndTurnActionOption(actions.AsValueEnumerable().First(action => action.Action is EndTurnActionDefinition));
    hud.ShowActionOptions([option]);
    var button = Required<Container>(hud, "%TurnButtons").GetChild<Button>(0);
    Assert.Equal("End Turn", button.Text);
    UnitActionOption? selected = null;
    hud.VerbSelected += value => selected = value;
    button.EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(option, selected));
    Assert.Equal(0, hud.GetNode<Container>("%VerbButtons").GetChildCount());
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task DialogueLowerThirdContainsPortraitAndScrollableLongLines(int width, int height)
  {
    var view = CreateDialogueView();
    var speaker = new SpeakerData
    {
      DisplayName = "Council Spokesperson",
      Portrait = GD.Load<PackedScene>("res://scenes/dialogue/PlaceholderPortrait.tscn"),
    };
    view.Configure(new DialogueSequenceData
    {
      Lines = [new DialogueLineData { Speaker = speaker, Text = new string('W', 4000) }],
    });
    var viewport = CreateUiViewport(view, new Vector2I(width, height));
    view.Advance();
    await WaitForLayout(viewport);

    var panel = view.GetNode<Control>("Panel");
    AssertInside(panel, width, height);
    Assert.True(ScreenRect(panel).Position.Y >= height * 0.55f);
    AssertInside(Required<Control>(view, "%Portrait"), width, height);
    var scroll = Required<ScrollContainer>(view, "%BodyScroll");
    AssertInside(scroll, width, height);
    Assert.True(scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page);
    Assert.False(view.HidesPreviousScene);
    Assert.True(view.GetNode<Label>("%Continue").Visible);
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task ResolutionKeepsRequiredActionsVisibleWithLongDescription(int width, int height)
  {
    var view = CreateResolutionView();
    var viewport = CreateUiViewport(view, new Vector2I(width, height));
    var definition = TestData.MakeEvent("A long transmission from the northern frontier");
    definition.Description = new string('W', 4000);
    using var campaign = GeoscapeFixture.WithFiredEvent(definition);
    campaign.OpenResolution(campaign.ActiveEvent);
    view.Present(campaign.State, campaign.Session);
    await WaitForLayout(viewport);

    AssertInside(Required<Control>(view, "%ResolutionPanel"), width, height);
    AssertInside(view.GetNode<HBoxContainer>("%Buttons"), width, height);
    var scroll = Required<ScrollContainer>(view, "%DescriptionScroll");
    Assert.True(scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page);
    Assert.True(view.BackButton is null);
    Assert.True(campaign.Session.PendingResolution.IsSome);
  }

  [TestCase]
  public async Task DialogueBodyClickAdvancesAndResetsLongLineScroll()
  {
    var view = CreateDialogueView();
    var speaker = new SpeakerData
    {
      DisplayName = "Spokesperson",
      Portrait = GD.Load<PackedScene>("res://scenes/dialogue/PlaceholderPortrait.tscn"),
    };
    view.Configure(new DialogueSequenceData
    {
      Lines = [
        new DialogueLineData { Speaker = speaker, Text = new string('W', 4000) },
        new DialogueLineData { Speaker = speaker, Text = "A new line." }],
    });
    var viewport = CreateUiViewport(view, new Vector2I(800, 600));
    view.Advance();
    await WaitForLayout(viewport);
    var scroll = Required<ScrollContainer>(view, "%BodyScroll");
    scroll.ScrollVertical = 200;
    Assert.True(scroll.ScrollVertical > 0);

    scroll.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
    {
      ButtonIndex = MouseButton.Left,
      Pressed = true,
    });

    Assert.Equal("A new line.", view.GetNode<Label>("%Body").Text);
    Assert.Equal(0, scroll.ScrollVertical);
  }

  private static T Required<T>(Node root, string path) where T : Node
  {
    T? node = root.GetNodeOrNull<T>(path);
    Assert.True(node is not null);
    return node!;
  }

  private static void AssertInside(Control control, int width, int height)
  {
    Rect2 rect = ScreenRect(control);
    Assert.True(rect.Position.X >= 0 && rect.Position.Y >= 0);
    Assert.True(rect.End.X <= width + 1 && rect.End.Y <= height + 1);
  }
}
