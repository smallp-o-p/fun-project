using FunProject.Core;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public partial class GeoscapeMapData : NamedEntityData
{
  [Export] public Vector2I Size { get; set; } = new(1600, 900);

  // ISO-8601; the runtime parses this (Godot cannot export DateTime).
  [Export] public string StartTimeIso { get; set; } = "2087-03-01T08:00:00";

  [Export] public RegionData[] Regions { get; set; } = [];

  [Export] public ScheduledEventData[] Timeline { get; set; } = [];
}
