using FunProject.Core;
using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class BaseArmorStat : Stat
{
  [Export] public Element Element { get; set; } = Element.Kinetic;
}
