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
  [Export] required public Array<StatMod> Modifiers { get; set; } = [];
};
