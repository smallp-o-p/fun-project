using Godot;

namespace FunProject.Dialogue;

[GlobalClass]
public partial class DialogueLineData : Resource
{
  [Export] public SpeakerData? Speaker { get; set; }

  [Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
}
