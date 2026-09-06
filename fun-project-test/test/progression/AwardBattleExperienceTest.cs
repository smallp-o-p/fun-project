using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Progression;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class AwardBattleExperienceTest
{
  private static FactionBattleSummary MakeSummary(
    Faction faction,
    IReadOnlySet<Combatant> present,
    IReadOnlyDictionary<Combatant, List<Combatant>> kills,
    IReadOnlySet<Combatant>? dead = null,
    BattleOutcome outcome = BattleOutcome.Victory)
    => new()
    {
      Faction = faction,
      Outcome = outcome,
      CombatantsPresent = present,
      DefeatedPerCombatant = kills,
      CombatantsDead = dead ?? new System.Collections.Generic.HashSet<Combatant>(),
      CombatantsWounded = new System.Collections.Generic.HashSet<Combatant>(),
      TurnCount = 1,
    };

  [TestCase(TestName = "Every present combatant earns participation XP")]
  public void ParticipationAwardedToAllPresent()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 10, actionPoints: 2, movement: 8);
    var bravo = TestData.MakeCombatant("Bravo", faction, health: 10, actionPoints: 2, movement: 8);
    var table = new ExperienceTableData { ParticipationXp = 10, KillXp = 25 };

    var awards = AwardBattleExperience.Award(
      MakeSummary(faction, new System.Collections.Generic.HashSet<Combatant> { alpha, bravo }, new Dictionary<Combatant, List<Combatant>>()),
      table);

    Assert.Equal(10, alpha.Rank.Xp);
    Assert.Equal(10, bravo.Rank.Xp);
    Assert.Equal(10, awards[alpha]);
    Assert.Equal(10, awards[bravo]);
  }

  [TestCase(TestName = "Kills add on top of participation per defeated count")]
  public void KillsAddOnTopOfParticipation()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 10, actionPoints: 2, movement: 8);
    var victim1 = TestData.MakeCombatant("Bandit1", TestData.MakeFaction("Raiders"));
    var victim2 = TestData.MakeCombatant("Bandit2", TestData.MakeFaction("Raiders"));

    var awards = AwardBattleExperience.Award(
      MakeSummary(faction,
        new System.Collections.Generic.HashSet<Combatant> { alpha },
        new Dictionary<Combatant, List<Combatant>> { [alpha] = [victim1, victim2] }),
      new ExperienceTableData { ParticipationXp = 10, KillXp = 25 });

    Assert.Equal(60, alpha.Rank.Xp); // 10 participation + 2 * 25 kills
    Assert.Equal(60, awards[alpha]);
  }

  [TestCase(TestName = "The fallen earn nothing — no participation, no posthumous kills")]
  public void DeadCombatantsEarnNothing()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 10, actionPoints: 2, movement: 8);
    var bravo = TestData.MakeCombatant("Bravo", faction, health: 10, actionPoints: 2, movement: 8);
    var charlie = TestData.MakeCombatant("Charlie", faction, health: 10, actionPoints: 2, movement: 8);
    var victim = TestData.MakeCombatant("Bandit", TestData.MakeFaction("Raiders"));

    var awards = AwardBattleExperience.Award(
      MakeSummary(faction,
        new System.Collections.Generic.HashSet<Combatant> { alpha, bravo, charlie },
        new Dictionary<Combatant, List<Combatant>>
        {
          [alpha] = [victim],
          [charlie] = [victim], // killed, then fell — no posthumous credit
        },
        dead: new System.Collections.Generic.HashSet<Combatant> { bravo, charlie }),
      new ExperienceTableData { ParticipationXp = 10, KillXp = 25 });

    Assert.Equal(35, alpha.Rank.Xp); // participation + kill
    Assert.Equal(0, bravo.Rank.Xp); // dead: no participation
    Assert.Equal(0, charlie.Rank.Xp); // dead: kill credit withheld too
    Assert.Equal(1, awards.Count);
    Assert.Equal(35, awards[alpha]);
  }

  [TestCase(TestName = "One battle is one gain per combatant — a single scaling pass, not per-achievement")]
  public void OneGainPerCombatantPerBattle()
  {
    var faction = TestData.MakeFaction("Player");
    // A 3% ladder: two separate gains of 10+25 would floor to 1 each (2 total); one
    // combined gain of 35 floors to max(1, 1) = 1. The distinction pins the contract.
    var slow = new RankTableData();
    slow.Levels.Add(new RankLevelData { Name = "Rookie", GainFactorPercent = 3 });
    slow.Levels.Add(new RankLevelData { Name = "Squaddie", GainFactorPercent = 3 });
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 10, actionPoints: 2, movement: 8,
      rankTable: slow);

    var victim = TestData.MakeCombatant("Bandit", TestData.MakeFaction("Raiders"));
    AwardBattleExperience.Award(
      MakeSummary(faction,
        new System.Collections.Generic.HashSet<Combatant> { alpha },
        new Dictionary<Combatant, List<Combatant>> { [alpha] = [victim] }),
      new ExperienceTableData { ParticipationXp = 10, KillXp = 25 });

    Assert.Equal(1, alpha.Rank.Xp);
  }

  [TestCase(TestName = "Malformed awards throw")]
  public void GuardsThrow()
  {
    var faction = TestData.MakeFaction("Player");
    Assert.Throws<System.InvalidOperationException>(() => AwardBattleExperience.Award(
      MakeSummary(faction, new System.Collections.Generic.HashSet<Combatant>(), new Dictionary<Combatant, List<Combatant>>()),
      new ExperienceTableData { ParticipationXp = 0, KillXp = 25 }));
  }
}
