using System;
using System.Collections.Generic;
using FunProject.Combatants;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public abstract partial class PredictionMarketData : Resource
{
  [Export] public uint MaxStake { get; set; } = 100;
  [Export] public double Multiplier { get; set; } = 1.5;
  public abstract PredictionMarket Instantiate(IReadOnlyList<Combatant> roster, Random random);
}
