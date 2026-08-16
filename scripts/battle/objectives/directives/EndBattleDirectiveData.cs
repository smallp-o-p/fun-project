using Godot;

namespace FunProject.Battle;

// Ends the battle with the authored outcome the moment the objective flips. No-ops if the
// battle already ended (first EndBattle wins).
[GlobalClass]
public sealed partial class EndBattleDirectiveData : ObjectiveDirectiveData
{
  [Export] public BattleOutcome Outcome { get; set; } = BattleOutcome.Victory;
}
