using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;

namespace FunProject.Progression;

/// <summary>
/// The post-battle XP award walk: participation XP for every surviving participant, kill
/// XP from the per-combatant attribution — the fallen earn nothing, not even their own
/// kills. Outcome-blind by design — the summary's Outcome is deliberately not consulted.
/// Pure: the caller owns when it runs (the campaign↔battle loop; tests today). Returns the
/// per-combatant totals for UI/logging.
/// </summary>
public static class AwardBattleExperience
{
  public static IReadOnlyDictionary<Combatant, int> Award(FactionBattleSummary summary, ExperienceTableData table)
  {
    ArgumentNullException.ThrowIfNull(summary);
    ArgumentNullException.ThrowIfNull(table);
    if (table.ParticipationXp < 1 || table.KillXp < 1)
      throw new InvalidOperationException(
        $"The experience table has ParticipationXp {table.ParticipationXp} / KillXp {table.KillXp}; authored awards must be at least 1.");

    var totals = new Dictionary<Combatant, int>();
    foreach (Combatant combatant in summary.CombatantsPresent)
    {
      if (summary.CombatantsDead.Contains(combatant))
        continue;
      totals[combatant] = table.ParticipationXp;
    }

    foreach ((Combatant killer, List<Combatant> defeated) in summary.DefeatedPerCombatant)
    {
      if (summary.CombatantsDead.Contains(killer))
        continue;
      totals[killer] = (totals.TryGetValue(killer, out int awarded) ? awarded : 0) + defeated.Count * table.KillXp;
    }

    foreach ((Combatant combatant, int award) in totals)
      combatant.Rank.Gain(award);
    return totals;
  }
}
