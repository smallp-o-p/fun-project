using Godot;

namespace FunProject.Dialogue;

[GlobalClass]
public partial class SpeakerData : Resource
{
  [Export] public string DisplayName { get; set; } = "";

  [Export] public PackedScene? Portrait { get; set; }
}
