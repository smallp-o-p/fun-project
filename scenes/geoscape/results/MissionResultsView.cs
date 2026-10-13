using FunProject.Battle;
using FunProject.Scenes;
using Godot;
using System.Globalization;

namespace FunProject.Geoscape;

// Keeps only display text after the battle runtime is disposed.
public sealed partial class MissionResultsView : GeoscapeView
{
  public void Bind(string missionType, FactionBattleSummary summary, BattleResult result)
  {
    GetNode<Label>("%MissionType").Text = missionType;
    GetNode<Label>("%TurnsTaken").Text = summary.TurnCount.ToString(CultureInfo.InvariantCulture);
    GetNode<Label>("%SoldiersWounded").Text = summary.CombatantsWounded.Count.ToString(CultureInfo.InvariantCulture);
    GetNode<Label>("%SoldiersKilled").Text = summary.CombatantsDead.Count.ToString(CultureInfo.InvariantCulture);
    int enemiesKilled = result.Factions.AsValueEnumerable()
      .Where(pair => pair.Key != summary.Faction).Sum(pair => pair.Value.Killed);
    GetNode<Label>("%EnemiesKilled").Text = enemiesKilled.ToString(CultureInfo.InvariantCulture);
  }
}
