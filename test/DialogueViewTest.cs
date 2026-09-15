#nullable disable warnings
using System;
using System.Threading.Tasks;
using FunProject.Dialogue;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class DialogueViewTest
{
  private static SpeakerData Speaker(string name = "Spokesman")
    => new()
    {
      DisplayName = name,
      Portrait = GD.Load<PackedScene>("res://scenes/dialogue/PlaceholderPortrait.tscn"),
    };

  private static DialogueSequenceData Sequence(params DialogueLineData[] lines)
    => new() { Lines = lines };

  private static DialogueLineData Line(SpeakerData speaker, string text)
    => new() { Speaker = speaker, Text = text };

  private static DialogueView MakeView(DialogueSequenceData sequence)
  {
    DialogueView view = CreateDialogueView();
    view.Configure(sequence);
    return AddToTree(view);
  }

  // Authoring-bug contracts call _Ready() directly (suite convention: the suite never
  // asserts throws across AddChild — ValidateSequence runs before any GetNode, so a
  // detached instance exercises the same path).
  private static void AssertReadyThrows(DialogueSequenceData sequence)
  {
    DialogueView view = CreateDialogueView();
    AutoFree(view);
    view.Configure(sequence);
    Assert.Throws<InvalidOperationException>(() => view._Ready());
  }

  // No awaits between tree entry and these assertions: the first _Process has not run,
  // so the body starts fully unrevealed and the portrait is the first line's model.
  [TestCase]
  public void InitialLineShowsSpeakerPortraitAndEmptyBody()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "Short line.")));

    Assert.Equal("Spokesman", view.GetNode<Label>("%SpeakerName").Text);
    Assert.Equal("Short line.", view.GetNode<Label>("%Body").Text);
    Assert.Equal(0, view.GetNode<Label>("%Body").VisibleCharacters);
    Assert.False(view.GetNode<Label>("%Continue").Visible);
    Assert.Equal(1, view.GetNode<Node3D>("%ModelRoot").GetChildCount());
  }

  [TestCase]
  public void RevealTickGrowsVisibleCharactersAndShowsIndicatorAtCompletion()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "12345678")));
    Label body = view.GetNode<Label>("%Body");

    view.TickReveal(0.1); // 40 cps -> 4 characters
    Assert.Equal(4, body.VisibleCharacters);
    Assert.False(view.GetNode<Label>("%Continue").Visible);

    view.TickReveal(0.1); // 8 characters = complete
    Assert.Equal(8, body.VisibleCharacters);
    Assert.True(view.GetNode<Label>("%Continue").Visible);
  }

  // Physics, not render process, drives the reveal: the layout wait's process frames
  // interleave physics ticks on the running SceneTree, and ~10 ticks of 1/60 s at 40 cps
  // reveal roughly 6 of 8 characters — advanced but not complete, with no direct
  // TickReveal call anywhere.
  [TestCase]
  public async Task PhysicsProcessDrivesReveal()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "12345678")));
    Label body = view.GetNode<Label>("%Body");

    await WaitForLayout(view);

    Assert.True(body.VisibleCharacters > 0);
    Assert.True(body.VisibleCharacters < body.GetTotalCharacterCount());
  }

  // "Commander 🛸" ends in a supplementary character: the label counts UTF-32
  // codepoints (VisibleCharacters and GetTotalCharacterCount agree, unlike C#
  // string.Length's UTF-16 units). VisibleCharacters stores the raw reveal cursor
  // without clamping to the total, so a tick sized to the label's own total completes
  // the reveal exactly on it.
  [TestCase]
  public void SupplementaryCharactersRevealCompletely()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "Commander 🛸")));
    Label body = view.GetNode<Label>("%Body");

    view.TickReveal(body.GetTotalCharacterCount() / view.CharactersPerSecond);

    Assert.Equal(body.GetTotalCharacterCount(), body.VisibleCharacters);
    Assert.True(view.GetNode<Label>("%Continue").Visible);
  }

  [TestCase]
  public void AdvanceWhileRevealingSnapsToFullText()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "A longer line of dialogue.")));

    view.Advance();

    Assert.Equal(-1, view.GetNode<Label>("%Body").VisibleCharacters);
    Assert.True(view.GetNode<Label>("%Continue").Visible);
  }

  [TestCase]
  public void AdvanceWhenCompleteMovesToNextLineAndSwapsPortrait()
  {
    SpeakerData first = Speaker("Alpha");
    SpeakerData second = Speaker("Beta");
    DialogueView view = MakeView(Sequence(Line(first, "One."), Line(second, "Two.")));
    Node firstModel = view.GetNode<Node3D>("%ModelRoot").GetChild(0);

    view.Advance(); // snap first line to full
    view.Advance(); // advance to the second line

    Assert.Equal("Beta", view.GetNode<Label>("%SpeakerName").Text);
    Label body = view.GetNode<Label>("%Body");
    Assert.Equal("Two.", body.Text);
    Assert.Equal(0, body.VisibleCharacters);
    Node3D modelRoot = view.GetNode<Node3D>("%ModelRoot");
    Assert.Equal(1, modelRoot.GetChildCount());
    Assert.False(ReferenceEquals(firstModel, modelRoot.GetChild(0)));
    Assert.False(GodotObject.IsInstanceValid(firstModel)); // freed now, not queued
  }

  [TestCase]
  public void FinalAdvanceFiresFinishedAndRequestsBack()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "Only line.")));
    int finished = 0;
    bool backRequested = false;
    view.Finished += () => finished++;
    view.BackRequested += () => backRequested = true;

    view.Advance(); // snap
    view.Advance(); // past the last line

    Assert.Equal(1, finished);
    Assert.True(backRequested);
  }

  [TestCase]
  public void DimClickAdvances()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker("A"), "One."), Line(Speaker("B"), "Two.")));

    view.OnDimGuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
    view.OnDimGuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });

    Assert.Equal("Two.", view.GetNode<Label>("%Body").Text);
  }

  // The panel's authored MouseFilter.Ignore values must let a real mouse event fall
  // through to the dim catcher, which advances like any other click (snap-to-full).
  [TestCase]
  public async Task ClickOverPanelAdvances()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "One line over the panel.")));
    await WaitForLayout(view);

    Vector2 click = ScreenRect(view.GetNode<Control>("Panel")).GetCenter();
    Input.ParseInputEvent(new InputEventMouseButton
    {
      Position = click,
      ButtonIndex = MouseButton.Left,
      Pressed = true,
    });
    await view.GetTree().ToSignal(view.GetTree(), SceneTree.SignalName.ProcessFrame);
    await view.GetTree().ToSignal(view.GetTree(), SceneTree.SignalName.ProcessFrame);

    Assert.Equal(-1, view.GetNode<Label>("%Body").VisibleCharacters);
    Assert.True(view.GetNode<Label>("%Continue").Visible);
  }

  // The panel anchors bottom-wide across the view: unpinned horizontal anchors
  // would expand it around x=0 and render the portrait offscreen.
  [TestCase]
  public async Task PanelAndPortraitStayOnscreen()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker(), "One line.")));
    await WaitForLayout(view);

    Vector2 viewport = view.GetViewportRect().Size;
    Rect2 panel = ScreenRect(view.GetNode<Control>("Panel"));
    Assert.True(panel.Position.X >= 0);
    Assert.True(panel.Position.Y >= 0);
    Assert.True(panel.End.X <= viewport.X);
    Assert.True(panel.End.Y <= viewport.Y);

    Rect2 portrait = ScreenRect(view.GetNode<Control>("Panel/HBox/Portrait"));
    Assert.True(portrait.Position.X >= 0);
    Assert.True(portrait.End.X <= viewport.X);
  }

  [TestCase]
  public void UiAcceptAdvances()
  {
    DialogueView view = MakeView(Sequence(Line(Speaker("A"), "One."), Line(Speaker("B"), "Two.")));

    view._Input(new InputEventAction { Action = "ui_accept", Pressed = true });
    view._Input(new InputEventAction { Action = "ui_accept", Pressed = true });

    Assert.Equal("Two.", view.GetNode<Label>("%Body").Text);
  }

  [TestCase]
  public void EmptySequenceThrows()
    => AssertReadyThrows(new DialogueSequenceData());

  [TestCase]
  public void MissingSpeakerThrows()
    => AssertReadyThrows(Sequence(new DialogueLineData { Text = "Nobody speaks this." }));

  [TestCase]
  public void MissingTextThrows()
    => AssertReadyThrows(Sequence(new DialogueLineData { Speaker = Speaker() }));

  [TestCase]
  public void SpeakerWithoutDisplayNameThrows()
    => AssertReadyThrows(Sequence(
      new DialogueLineData { Speaker = new SpeakerData { Portrait = Speaker().Portrait }, Text = "Anonymous." }));

  [TestCase]
  public void SpeakerWithoutPortraitThrows()
    => AssertReadyThrows(Sequence(
      new DialogueLineData { Speaker = new SpeakerData { DisplayName = "Faceless" }, Text = "Unseen." }));

  [TestCase]
  public void ReadyWithoutConfigureThrows()
  {
    DialogueView view = CreateDialogueView();
    AutoFree(view);
    Assert.Throws<InvalidOperationException>(() => view._Ready());
  }

  [TestCase]
  public void DoubleConfigureThrows()
  {
    DialogueView view = CreateDialogueView();
    AutoFree(view);
    view.Configure(Sequence(Line(Speaker(), "Hi.")));
    Assert.Throws<InvalidOperationException>(() => view.Configure(Sequence(Line(Speaker(), "Again."))));
  }
}
