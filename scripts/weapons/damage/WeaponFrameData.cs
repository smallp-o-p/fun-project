using FunProject.Core;
using Godot;
using Godot.Collections;

namespace FunProject.Weapons;

[GlobalClass]
public partial class WeaponFrameData : NamedEntityData
{
  [Export] required public Array<DamagePacketData> Packets { get; set; } = [];
}
