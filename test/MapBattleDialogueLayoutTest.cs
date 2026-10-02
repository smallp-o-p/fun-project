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
  public async Task GeoscapeUsesOneCompactBottomRowAndTopLeftTime(int width, int height)
  {
    var hud = CreateHud();
    var viewport = CreateUiViewport(hud, new Vector2I(width, height));
    var item = TestData.MakeItemData(new string('W', 160), manufacturingDays: 2);
    using var campaign = new GeoscapeFixture(TestData.MakeStart(manufacturableItems: [item]));
    Assert.True(campaign.Session.StartManufacturing(item).IsRight);
    hud.UpdateManufacturing(campaign.Session.ActiveManufacturing, 1);
    await WaitForLayout(viewport);

    var bottom = hud.GetNode<Control>("BottomBar");
    AssertInside(bottom, width, height);
    Assert.True(bottom.Size.Y <= 64, "The bottom HUD must remain one compact row.");
    foreach (string name in new[] { "UnitsButton", "EngineeringButton", "CaptivityButton", "EngineeringProgress", "EngineeringRemaining" })
    {
      var control = hud.GetNode<Control>($"%{name}");
      AssertInside(control, width, height);
      Assert.True(ScreenRect(control).Position.Y > height * 0.85f);
      Assert.True(Mathf.Abs(ScreenRect(control).GetCenter().Y - ScreenRect(bottom).GetCenter().Y) < 1);
    }
    foreach (string name in new[] { "PauseButton", "SpeedButton", "ClockLabel" })
    {
      var control = hud.GetNode<Control>($"%{name}");
      AssertInside(control, width, height);
      Assert.True(ScreenRect(control).Position.Y < 80);
      Assert.True(ScreenRect(control).End.X < width * 0.5f);
    }
    var progress = hud.GetNode<Label>("%EngineeringProgress");
    Assert.True(progress.ClipText);
    Assert.Equal(item.Name, progress.TooltipText);
    Assert.Equal(Control.MouseFilterEnum.Pass, progress.MouseFilter);
    var remaining = hud.GetNode<Label>("%EngineeringRemaining");
    Assert.Equal("2d remaining", remaining.Text);
    Assert.True(ScreenRect(remaining).End.X > width - 40, "Manufacturing stays at the right edge.");
    Assert.True(ScreenRect(remaining).End.X - ScreenRect(progress).Position.X <= 340,
      "Manufacturing remains compact instead of taking all spare row width.");
    Assert.Equal(HorizontalAlignment.Right, progress.HorizontalAlignment);
    Assert.False(hud.HasNode("AlertsPanel"));
    Assert.False(hud.HasNode("Heading"));

    hud.UpdateManufacturing(None, 3000);
    await WaitForLayout(viewport);
    Assert.True(ScreenRect(progress).End.X > width - 40, "The idle summary stays at the right edge.");
    Assert.True(progress.Size.X <= 220, "The idle summary remains compact.");
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task ManufacturingDividerStaysCloseToShortItemAndIdleText(int width, int height)
  {
    var hud = CreateHud();
    var viewport = CreateUiViewport(hud, new Vector2I(width, height));
    var item = TestData.MakeItemData("Prototype Kit", manufacturingDays: 2);
    using var campaign = new GeoscapeFixture(TestData.MakeStart(manufacturableItems: [item]));
    Assert.True(campaign.Session.StartManufacturing(item).IsRight);
    hud.UpdateManufacturing(campaign.Session.ActiveManufacturing, 1);
    await WaitForLayout(viewport);

    var progress = hud.GetNode<Label>("%EngineeringProgress");
    var separator = hud.GetNode<Control>("BottomBar/Margin/Row/Separator");
    float textStart = ScreenRect(progress).Position.X + progress.GetCharacterBounds(0).Position.X;
    Assert.True(textStart - ScreenRect(separator).End.X <= 18,
      "The divider must sit beside the item text without a reserved blank name column.");
    Assert.True(progress.Size.X > 80, "The item name must remain readable.");
    Assert.True(progress.Size.X < 130, "Short item names use only their content width.");

    float originalWidth = progress.Size.X;
    progress.AddThemeFontSizeOverride("font_size", 20);
    await WaitForLayout(viewport);
    Assert.True(progress.Size.X > originalWidth, "Content sizing follows a changed theme font size.");
    Assert.True(progress.GetCharacterBounds(progress.Text.Length - 1).End.X <= progress.Size.X,
      "The final character stays visible after a theme change.");
    progress.RemoveThemeFontSizeOverride("font_size");

    hud.UpdateManufacturing(None, 3000);
    await WaitForLayout(viewport);
    textStart = ScreenRect(progress).Position.X + progress.GetCharacterBounds(0).Position.X;
    Assert.True(textStart - ScreenRect(separator).End.X <= 18,
      "The divider also stays beside the idle summary.");
    Assert.True(ScreenRect(progress).End.X > width - 40);
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
