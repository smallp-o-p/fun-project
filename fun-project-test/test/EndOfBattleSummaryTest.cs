using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class EndOfBattleSummaryTest
{
  [TestCase(TestName = "The summary query fails while the battle has not ended")]
  public void SummaryQueryFailsWhileBattleInProgress()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      PlayerControlled = true,
      Player = new("Alpha", Position: new Vector3I(0, 0, 0)),
      Enemy = new("Bandit", Position: new Vector3I(2, 0, 0)),
    }.Start();

    var failure = GetFailure(Query(battle.Session, new GetFactionEndOfBattleSummary(battle.PlayerFaction)));
    Assert.Equal(BattleQueryFailureReason.InvalidBattleState, failure.Reason);
  }

  [TestCase(TestName = "A victory summary tallies kills per combatant and faction-filtered dead and wounded")]
  public void VictorySummaryTalliesKillsDeadAndWounded()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(8, 1, 8),
      HitChanceCalculator = new AlwaysHitCalculator(),
      PlayerControlled = true,
      Player = new("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle", damage: 10)),
      Enemy = new("Bandit1", Health: 10),
    }.Start();
    var bravo = SpawnUnit(battle.Session, BattleTestFactory.MakeCombatant("Bravo", battle.PlayerFaction), new Vector3I(0, 0, 0));
    var charlie = SpawnUnit(battle.Session, BattleTestFactory.MakeCombatant("Charlie", battle.PlayerFaction), new Vector3I(1, 0, 0));
    var bandit2 = SpawnUnit(battle.Session, BattleTestFactory.MakeCombatant("Bandit2", battle.EnemyFaction, health: 10), new Vector3I(5, 0, 4));

    ApplyDamage(battle.Session, bravo, 999);
    ApplyDamage(battle.Session, charlie, 1);
    Attack(battle.Session, battle.Executor, battle.PlayerUnit, battle.EnemyUnit);
    Attack(battle.Session, battle.Executor, battle.PlayerUnit, bandit2);
    EndFactionTurn(battle.Session, battle.PlayerFaction);
    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);

    var summary = GetValue(Query(battle.Session, new GetFactionEndOfBattleSummary(battle.PlayerFaction)));

    Assert.Equal(battle.PlayerFaction, summary.Faction);
    Assert.Equal(BattleOutcome.Victory, summary.Outcome);
    Assert.Equal(battle.Session.TurnNumber, summary.TurnCount);
    Assert.True(summary.CombatantsDead.SetEquals([bravo.Combatant]), "Dead should hold only the fallen player combatant, not enemy dead.");
    Assert.True(summary.CombatantsWounded.SetEquals([charlie.Combatant]), "Wounded should hold only the hurt-but-alive player combatant.");
    Assert.True(summary.CombatantsPresent.SetEquals(
      [battle.PlayerUnit.Combatant, bravo.Combatant, charlie.Combatant]),
      "Present should hold every faction combatant — dead, wounded, and untouched alike.");
    Assert.Equal(1, summary.DefeatedPerCombatant.Count);
    Assert.True(summary.DefeatedPerCombatant[battle.PlayerUnit.Combatant].AsValueEnumerable().SequenceEqual([battle.EnemyUnit.Combatant, bandit2.Combatant]));
  }

  [TestCase(TestName = "A defeat summary excludes enemy wounded and the enemy summary attributes its kill")]
  public void DefeatSummaryFiltersByFactionAndAttributesEnemyKill()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(8, 1, 8),
      HitChanceCalculator = new AlwaysHitCalculator(),
      PlayerControlled = true,
      Player = new("Alpha", Health: 5),
      Enemy = new("Bandit", Position: new Vector3I(4, 0, 3), Weapon: BattleTestFactory.MakeWeapon("Shiv", damage: 5)),
    }.Start();

    ApplyDamage(battle.Session, battle.EnemyUnit, 1);
    EndFactionTurn(battle.Session, battle.PlayerFaction);
    Attack(battle.Session, battle.Executor, battle.EnemyUnit, battle.PlayerUnit);
    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);

    var playerSummary = GetValue(Query(battle.Session, new GetFactionEndOfBattleSummary(battle.PlayerFaction)));
    Assert.Equal(BattleOutcome.Defeat, playerSummary.Outcome);
    Assert.True(playerSummary.CombatantsDead.SetEquals([battle.PlayerUnit.Combatant]));
    Assert.Equal(0, playerSummary.CombatantsWounded.Count, "The wounded enemy must not appear in the player's summary.");
    Assert.Equal(0, playerSummary.DefeatedPerCombatant.Count);

    var enemySummary = GetValue(Query(battle.Session, new GetFactionEndOfBattleSummary(battle.EnemyFaction)));
    Assert.Equal(0, enemySummary.CombatantsDead.Count);
    Assert.True(enemySummary.CombatantsWounded.SetEquals([battle.EnemyUnit.Combatant]));
    Assert.Equal(1, enemySummary.DefeatedPerCombatant.Count);
    Assert.True(enemySummary.DefeatedPerCombatant[battle.EnemyUnit.Combatant].AsValueEnumerable().SequenceEqual([battle.PlayerUnit.Combatant]));
  }
}
