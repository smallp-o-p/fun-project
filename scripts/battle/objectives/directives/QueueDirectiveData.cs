using Godot;

namespace FunProject.Battle;

// Instantiates and adds each follow-up objective to the flipping objective's faction. Each
// follow-up is evaluated from the NEXT committed event after it is added (never the one
// that queued it).
[GlobalClass]
public sealed partial class QueueDirectiveData : ObjectiveDirectiveData
{
  [Export] public ObjectiveData[] FollowUps { get; set; } = [];
}
