using System.Diagnostics.CodeAnalysis;
using FunProject.Core;
using FunProject.Stats;
using Godot;
using Godot.Collections;
namespace FunProject.Weapons;

[GlobalClass]
[method: SetsRequiredMembers]
public partial class Ammunition() : NamedEntityData
{
  [Export] public Array<StatMod> Modifiers { get; set; } = [];
  [Export] public Array<DamageBundleMod> DamageMods { get; set; } = [];
};
