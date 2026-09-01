using FunProject.Core;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public partial class GeoscapeMapData : NamedEntityData
{
  [Export] public Vector2I Size { get; set; } = new(1600, 900);

  // ISO-8601; the runtime parses this (Godot cannot export DateTime).
  [Export] public int StartingDay { get; set; } = 1;
  [Export] public RegionData[] Regions { get; set; } = [];

  [Export] public ScheduledEventData[] Timeline { get; set; } = [];
}
