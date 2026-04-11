using FunProject.Items.Effects;
using Godot.Collections;

namespace FunProject.Items;

public class Grenade : ThrowableItem
{
  public int BlastRadius { get; }
  public Array<BattleEffectData> Effects { get; }

  public Grenade(GrenadeData data) : base(data)
  {
    BlastRadius = data.BlastRadius;
    Effects = data.Effects;
  }
}
