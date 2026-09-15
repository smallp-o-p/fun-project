#nullable disable warnings
using FunProject.Dialogue;
using FunProject.Strategic;
using Godot;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class DialogueAuthoringTest
{
  // Pins the debug bring-up wiring: the Council Broadcast plot event in the authored
  // test map carries a playable sequence (every line spoken, every speaker portraited).
  // Uses the project's custom Assert (bool/value helpers), not GdUnit4's fluent API.
  [TestCase]
  public void CouncilBroadcastEventCarriesPlayableDialogue()
  {
    var map = GD.Load<GeoscapeMapData>("res://resources/geoscape/TestMap.tres");
    GeoscapeEventDefinition council = map.Timeline
      .AsValueEnumerable().Select(schedule => schedule.Event)
      .Single(definition => definition.Title == "Council Broadcast");

    DialogueSequenceData dialogue = council.Dialogue
      ?? throw new System.InvalidOperationException(
        "Council Broadcast must carry a Dialogue sequence.");
    Assert.True(dialogue.Lines.Length >= 2);
    foreach (DialogueLineData line in dialogue.Lines)
    {
      Assert.That(line.Speaker is not null, "every line needs a speaker");
      Assert.Equal("Council Spokesman", line.Speaker!.DisplayName);
      Assert.That(line.Speaker.Portrait is not null, "every speaker needs a portrait");
      Assert.False(string.IsNullOrEmpty(line.Text), "every line needs text");
    }
  }
}
