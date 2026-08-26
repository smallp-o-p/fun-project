using FunProject.Strategic;
using Godot;

// Godot-signal envelope for the ActiveGeoscapeEvent record (signals carry only Variant-
// compatible payloads; the record is plain C#). Battle-layer precedent: BattleEventAdapter.
public sealed partial class GeoscapeEventAdapter : RefCounted
{
  public required GeoscapeEvent Event { get; init; }
}
