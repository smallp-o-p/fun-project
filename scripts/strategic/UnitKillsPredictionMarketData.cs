using System;
using System.Collections.Generic;
using System.Globalization;
using FunProject.Combatants;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public sealed partial class UnitKillsPredictionMarketData : PredictionMarketData
{
  public UnitKillsPredictionMarketData() => DescriptionFormat = "{0} scores at least {1} kill(s)";

  private uint _maximumKills = 3;
  [Export] public uint MinimumKills { get; set; } = 1;
  [Export] public uint MaximumKills { get => Math.Max(MinimumKills, _maximumKills); set => _maximumKills = value; }

  public override PredictionMarket Instantiate(IReadOnlyList<Combatant> roster, Random random)
  {
    uint required = (uint)random.NextInt64(MinimumKills, (long)MaximumKills + 1);
    var unit = roster[random.Next(roster.Count)];
    return new UnitKillsPredictionMarket(unit, required,
      string.Format(CultureInfo.InvariantCulture, DescriptionFormat, unit.Name, required), MaxStake, (decimal)Multiplier);
  }
}
