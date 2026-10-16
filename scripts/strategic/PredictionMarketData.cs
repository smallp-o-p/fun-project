using System;
using System.Collections.Generic;
using FunProject.Combatants;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public abstract partial class PredictionMarketData : Resource
{
  // {0} is the selected unit's name; kill-count markets also supply {1} as the required kills.
  [Export(PropertyHint.MultilineText)] public string DescriptionFormat { get; set; } = "{0}";
  [Export] public uint MaxStake { get; set; } = 100;
  [Export] public double Multiplier { get; set; } = 1.5;
  public abstract PredictionMarket Instantiate(IReadOnlyList<Combatant> roster, Random random);
}
