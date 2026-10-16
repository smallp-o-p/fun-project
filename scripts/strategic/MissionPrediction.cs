using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Currencies;

namespace FunProject.Strategic;

public abstract record PredictionMarket(uint MaxStake, decimal Multiplier)
{
  public abstract string Description { get; }
  public ulong TotalReturn(ulong stake) => (ulong)(stake * Multiplier);
  internal abstract bool IsCorrect(MissionDeployment deployment, FactionBattleSummary summary);
}

public sealed record DeployUnitPredictionMarket(Combatant Unit, uint MaxStake, decimal Multiplier)
  : PredictionMarket(MaxStake, Multiplier)
{
  public override string Description => $"{Unit.Name} deploys";
  internal override bool IsCorrect(MissionDeployment deployment, FactionBattleSummary summary)
    => deployment.Participants.AsValueEnumerable().Any(member => ReferenceEquals(member, Unit));
}

public sealed record UnitKillsPredictionMarket(Combatant Unit, uint MinimumKills, uint MaxStake, decimal Multiplier)
  : PredictionMarket(MaxStake, Multiplier)
{
  public override string Description => $"{Unit.Name} scores at least {MinimumKills} kill(s)";
  internal override bool IsCorrect(MissionDeployment deployment, FactionBattleSummary summary)
    => deployment.Participants.AsValueEnumerable().Any(member => ReferenceEquals(member, Unit))
      && (summary.DefeatedPerCombatant.TryGetValue(Unit, out var kills) ? kills.Count : 0) >= MinimumKills;
}

public sealed record MissionPrediction(PredictionMarket Market, Currency Currency, ulong Amount)
{
  public bool Correct { get; internal set; }
  public ulong Returned => Correct ? Market.TotalReturn(Amount) : 0;
}
