using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public partial class ScheduledEventData : Resource
{
  [Export] public int AtTick { get; set; }

  [Export] public GeoscapeEventDefinition? Event { get; set; }
}
