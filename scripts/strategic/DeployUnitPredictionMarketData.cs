using System;
using System.Collections.Generic;
using System.Globalization;
using FunProject.Combatants;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public sealed partial class DeployUnitPredictionMarketData : PredictionMarketData
{
  public DeployUnitPredictionMarketData() => DescriptionFormat = "{0} deploys";

  public override PredictionMarket Instantiate(IReadOnlyList<Combatant> roster, Random random)
  {
    var unit = roster[random.Next(roster.Count)];
    return new DeployUnitPredictionMarket(unit,
      string.Format(CultureInfo.InvariantCulture, DescriptionFormat, unit.Name), MaxStake, (decimal)Multiplier);
  }
}
