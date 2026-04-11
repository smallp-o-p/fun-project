using FunProject.Items.Effects;
using Godot;
using Godot.Collections;

namespace FunProject.Items;

[GlobalClass]
public partial class GrenadeData : ThrowableItemData
{
  [Export] public int BlastRadius { get; set; } = 1;
  [Export] public Array<BattleEffectData> Effects { get; set; } = [];
}
