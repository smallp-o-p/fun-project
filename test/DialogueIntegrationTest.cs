#nullable disable warnings
using System.Threading.Tasks;
using FunProject.Dialogue;
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class DialogueIntegrationTest
{
  private static DialogueSequenceData CouncilDialogue() => new()
  {
    Lines =
    [
      new DialogueLineData
      {
        Speaker = new SpeakerData
        {
          DisplayName = "Spokesman",
          Portrait = GD.Load<PackedScene>("res://scenes/dialogue/PlaceholderPortrait.tscn"),
        },
        Text = "First.",
      },
      new DialogueLineData
      {
        Speaker = new SpeakerData
        {
          DisplayName = "Spokesman",
          Portrait = GD.Load<PackedScene>("res://scenes/dialogue/PlaceholderPortrait.tscn"),
        },
        Text = "Second.",
      },
    ]
  };

  private static (GeoscapeScene Scene, GeoscapeViewManager Manager) SceneWithEvent(
    GeoscapeEventDefinition definition)
  {
    GeoscapeScene scene = AddToTree(CreateGeoscapeScene(
      MakeStart(timeline: [MakeScheduled(1, definition)])));
    GeoscapeViewManager manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed); // 5x, running
    scene._PhysicsProcess(0.1); // a single step fires the tick-1 event
    return (scene, manager);
  }

  // The dialogue-carrying event pushes resolution + dialogue: the dialogue covers the
  // retained resolution view, and finishing it pops back to that resolution.
  [TestCase]
  public async Task OpeningDialogueEventPlaysDialogueOverResolutionThenPopsBack()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = SceneWithEvent(MakeEvent("Council Broadcast", dialogue: CouncilDialogue()));
    OpenResolutionViaAlert(scene);

    Assert.True(manager.Current is DialogueView);
    Assert.Equal("Spokesman", manager.Current.GetNode<Label>("%SpeakerName").Text);
    GeoscapeEventResolution resolution = manager.GetChildren()
      .AsValueEnumerable().OfType<GeoscapeEventResolution>().Single();
    // The dialogue overlays without hiding (HidesPreviousScene = false): the retained
    // resolution still renders beneath the dim wash, like the dialog over the map root.
    Assert.True(resolution.IsVisibleInTree());

    var dialogue = (DialogueView)manager.Current;
    dialogue.Advance(); // snap the first line to full
    dialogue.Advance(); // advance to the second line
    dialogue.Advance(); // snap the second line to full
    dialogue.Advance(); // past the last: Finished + RequestBack -> manager popped it

    Assert.True(manager.Current is GeoscapeEventResolution);
    Assert.True(dialogue.GetParent() is null); // popped views detach immediately
    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(dialogue)); // ...and are freed by the deferred flush
  }

  [TestCase]
  public async Task OpeningPlainEventPushesResolutionDirectly()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = SceneWithEvent(MakeEvent("Quiet plot"));

    OpenResolutionViaAlert(scene);

    Assert.True(manager.Current is GeoscapeEventResolution);
  }

  [TestCase]
  public void MissingDialogueViewSceneExportThrowsAtReady()
  {
    GeoscapeScene scene = CreateGeoscapeScene(MakeStart());
    AutoFree(scene); // _Ready throws before tree entry; the subtree would leak
    scene.DialogueViewScene = null;
    Assert.Throws<System.InvalidOperationException>(() => scene._Ready());
  }
}
