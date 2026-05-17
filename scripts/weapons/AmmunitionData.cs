using System.Diagnostics.CodeAnalysis;
using FunProject.Core;
using FunProject.Stats;
using Godot;
using Godot.Collections;
namespace FunProject.Weapons;

[GlobalClass]

public partial class AmmunitionData : NamedEntityData
{
  [Export] required public Array<StatMod> Modifiers { get; set; } = [];

  [SetsRequiredMembers]
  public AmmunitionData()
  {
    Modifiers = [];
  }
};
