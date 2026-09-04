using Godot;

namespace FunProject.Progression;

/// <summary>
/// Authored XP awards per achievement. New achievement kinds = a new field plus a
/// collection site in <see cref="AwardBattleExperience"/> — the award walk stays a pure
/// function of this table and the battle summary.
/// </summary>
[GlobalClass]
public partial class ExperienceTableData : Resource
{
  [Export] public int ParticipationXp { get; set; } = 10;
  [Export] public int KillXp { get; set; } = 25;
}
