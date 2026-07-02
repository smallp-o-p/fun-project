using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Linq;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class EndOfBattleSummaryTest
{
  [TestCase(TestName = "The summary query fails while the battle has not ended")]
  public void SummaryQueryFailsWhileBattleInProgress()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit", enemy), new Vector3I(2, 0, 0));
    StartBattle(session);

    var failure = GetFailure(Query(session, new GetFactionEndOfBattleSummary(player)));
    Assert.Equal(BattleQueryFailureReason.InvalidBattleState, failure.Reason);
  }

  [TestCase(TestName = "A victory summary tallies kills per combatant and faction-filtered dead and wounded")]
  public void VictorySummaryTalliesKillsDeadAndWounded()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [player, enemy], new AlwaysHitCalculator(), playerFaction: Some(player));
    var alpha = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", player), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle", damage: 10));
    var bravo = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", player), new Vector3I(0, 0, 0));
    var charlie = SpawnUnit(session, BattleTestFactory.MakeCombatant("Charlie", player), new Vector3I(1, 0, 0));
    var bandit1 = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit1", enemy, health: 10), new Vector3I(4, 0, 4));
    var bandit2 = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit2", enemy, health: 10), new Vector3I(5, 0, 4));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.ApplyDamage(bravo.State, 999)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.ApplyDamage(charlie.State, 1)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.AttackUnit(alpha.State, bandit1.State)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.AttackUnit(alpha.State, bandit2.State)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.EndFactionTurn(player)).RequireSingleResult().Succeeded);
    Assert.Equal(BattlePhase.Ended, session.Phase);

    var summary = GetValue(Query(session, new GetFactionEndOfBattleSummary(player)));

    Assert.Equal(player, summary.Faction);
    Assert.Equal(BattleOutcome.Victory, summary.Outcome);
    Assert.Equal(session.TurnNumber, summary.TurnCount);
    Assert.True(summary.CombatantsDead.SetEquals([bravo.Combatant]), "Dead should hold only the fallen player combatant, not enemy dead.");
    Assert.True(summary.CombatantsWounded.SetEquals([charlie.Combatant]), "Wounded should hold only the hurt-but-alive player combatant.");
    Assert.Equal(1, summary.DefeatedPerCombatant.Count);
    Assert.True(summary.DefeatedPerCombatant[alpha.Combatant].SequenceEqual([bandit1.Combatant, bandit2.Combatant]));
  }

  [TestCase(TestName = "A defeat summary excludes enemy wounded and the enemy summary attributes its kill")]
  public void DefeatSummaryFiltersByFactionAndAttributesEnemyKill()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [player, enemy], new AlwaysHitCalculator(), playerFaction: Some(player));
    var alpha = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", player, health: 5), new Vector3I(4, 0, 1));
    var bandit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit", enemy), new Vector3I(4, 0, 3), BattleTestFactory.MakeWeapon("Shiv", damage: 5));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.ApplyDamage(bandit.State, 1)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.EndFactionTurn(player)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.AttackUnit(bandit.State, alpha.State)).RequireSingleResult().Succeeded);
    Assert.Equal(BattlePhase.Ended, session.Phase);

    var playerSummary = GetValue(Query(session, new GetFactionEndOfBattleSummary(player)));
    Assert.Equal(BattleOutcome.Defeat, playerSummary.Outcome);
    Assert.True(playerSummary.CombatantsDead.SetEquals([alpha.Combatant]));
    Assert.Equal(0, playerSummary.CombatantsWounded.Count, "The wounded enemy must not appear in the player's summary.");
    Assert.Equal(0, playerSummary.DefeatedPerCombatant.Count);

    var enemySummary = GetValue(Query(session, new GetFactionEndOfBattleSummary(enemy)));
    Assert.Equal(0, enemySummary.CombatantsDead.Count);
    Assert.True(enemySummary.CombatantsWounded.SetEquals([bandit.Combatant]));
    Assert.Equal(1, enemySummary.DefeatedPerCombatant.Count);
    Assert.True(enemySummary.DefeatedPerCombatant[bandit.Combatant].SequenceEqual([alpha.Combatant]));
  }
}
