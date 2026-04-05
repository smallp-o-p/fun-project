using FunProject.Core;
using FunProject.Stats;
using Godot;
using Godot.Collections;
namespace FunProject.Weapons;

[GlobalClass]

public partial class AmmunitionData : NamedEntityData
{
  [Export] public Array<StatModifier> Modifiers { get; set; }
};
