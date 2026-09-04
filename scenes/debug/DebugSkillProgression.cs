using FunProject.Combatants;
using Godot;

// Debug bring-up: opens the skill progression view standalone with a freshly-built soldier,
// so the whole flow (commit, earn debug points, unlock) can be exercised from the editor.
public sealed partial class DebugSkillProgression : Control
{
  public override void _Ready()
  {
    var faction = new Faction(new FactionData { Name = "Xeno Division" });
    var soldier = new Combatant(new CombatantData
    {
      Name = "Cpl. Reyes",
      HealthStat = new FunProject.Stats.HealthStat { BaseValue = 20 },
      ActionPointsStat = new FunProject.Stats.ActionPointsStat { BaseValue = 4 },
      WillStat = new FunProject.Stats.WillStat { BaseValue = 50 },
      MovementStat = new FunProject.Stats.MovementStat { BaseValue = 12 },
      VisionStat = new FunProject.Stats.VisionStat { BaseValue = 20 },
      AimStat = new FunProject.Stats.AimStat { BaseValue = 65 },
    }, faction);
    soldier.Progression.AwardPoints(3);

    var view = GetNode<SkillProgressionView>("SkillProgressionView");
    view.Visible = true;
    view.Present(soldier);
  }
}
