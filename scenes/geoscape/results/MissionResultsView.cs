using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Scenes;
using FunProject.Strategic;
using Godot;
using System.Globalization;

namespace FunProject.Geoscape;

// Keeps only display text after the battle runtime is disposed.
public sealed partial class MissionResultsView : GeoscapeView
{
  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    if (session.LastMissionReceipt.Case is MissionReceipt receipt) Bind(receipt);
    else
    {
      GetNode<Label>("%Title").Text = "No mission results yet.";
      foreach (string name in new[] { "MissionType", "TurnsTaken", "SoldiersWounded", "SoldiersKilled", "EnemiesKilled", "Rewards" })
        GetNode<Label>($"%{name}").Text = "—";
    }
  }

  public void Bind(MissionReceipt receipt)
  {
    MissionTally? tally = receipt.Tally.Case as MissionTally;
    GetNode<Label>("%Title").Text = $"Mission Results — {receipt.Outcome.Match(outcome => outcome.ToString(), () => "Abandoned")}";
    GetNode<Label>("%MissionType").Text = receipt.MissionType;
    GetNode<Label>("%TurnsTaken").Text = tally?.TurnsTaken.ToString(CultureInfo.InvariantCulture) ?? "—";
    GetNode<Label>("%SoldiersWounded").Text = tally?.SoldiersWounded.ToString(CultureInfo.InvariantCulture) ?? "—";
    GetNode<Label>("%SoldiersKilled").Text = tally?.SoldiersKilled.ToString(CultureInfo.InvariantCulture) ?? "—";
    GetNode<Label>("%EnemiesKilled").Text = tally?.EnemiesKilled.ToString(CultureInfo.InvariantCulture) ?? "—";
    GetNode<Label>("%Rewards").Text = "Ordinary reward: " + (receipt.Rewards.Count == 0 ? "None"
      : string.Join(", ", receipt.Rewards.AsValueEnumerable().Select(pair => $"+{pair.Value} {pair.Key.Name}").ToArray()));

  }
}
