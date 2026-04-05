using System;
using FunProject.Stats;
using Godot.Collections;

namespace FunProject.Weapons;

public struct Ammunition(AmmunitionData data)
{
  public Array<StatModifier> statModifiers = data.Modifiers;
  public string AmmoName = data.Name;
  public string AmmoDescription = data.Description;
};
