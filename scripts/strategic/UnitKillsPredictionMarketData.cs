using System;
using System.Collections.Generic;
using FunProject.Combatants;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public sealed partial class UnitKillsPredictionMarketData : PredictionMarketData
{
  private uint _maximumKills = 3;
  [Export] public uint MinimumKills { get; set; } = 1;
  [Export] public uint MaximumKills { get => Math.Max(MinimumKills, _maximumKills); set => _maximumKills = value; }

  public override PredictionMarket Instantiate(IReadOnlyList<Combatant> roster, Random random)
  {
    uint required = (uint)random.NextInt64(MinimumKills, (long)MaximumKills + 1);
    return new UnitKillsPredictionMarket(roster[random.Next(roster.Count)], required, MaxStake, (decimal)Multiplier);
  }
}
