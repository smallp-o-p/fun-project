using System;
using System.Collections.Generic;
using FunProject.Combatants;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public sealed partial class DeployUnitPredictionMarketData : PredictionMarketData
{
  public override PredictionMarket Instantiate(IReadOnlyList<Combatant> roster, Random random)
    => new DeployUnitPredictionMarket(roster[random.Next(roster.Count)], MaxStake, (decimal)Multiplier);
}
