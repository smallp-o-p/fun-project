using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class EndOfBattleSummaryTest
{
  [TestCase]
  public void CapturesAreAvailableBeforeSessionEndedIsBroadcast()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    int endedEvents = 0;
    battle.Runtime.BattleEventCommitted += evt =>
    {
      if (evt is not SessionEndedBattleEvent)
        return;
      endedEvents++;
      var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
      Assert.Equal(1, summary.CapturedEnemies.Count);
      Assert.True(ReferenceEquals(battle.EnemyUnit.Combatant, summary.CapturedEnemies[0]));
    };

    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Equal(1, endedEvents);
  }

  [TestCase]
  public void CapturedMembershipIsFrozenAtTheFirstEndBattle()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    var captured = battle.EnemyUnit.Combatant;

    // Deliberate internal mutation probes frozen membership; this is not a supported gameplay action.
    battle.EnemyUnit.ReceiveDamage(20);
    captured.OwningFaction = battle.PlayerFaction;
    battle.Session.EndBattle(BattleOutcome.Defeat);
    var after = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();

    Assert.Equal(1, after.CapturedEnemies.Count);
    Assert.True(ReferenceEquals(captured, after.CapturedEnemies[0]));
  }

  [TestCase]
  public void OnlyTheDesignatedPlayerSummaryReceivesVictoryCaptures()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Equal(1, battle.Query(
      new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight().CapturedEnemies.Count);
    Assert.Equal(0, battle.Query(
      new GetFactionEndOfBattleSummary(battle.EnemyFaction)).RequireRight().CapturedEnemies.Count);
    Assert.Equal(0, battle.Query(
      new GetFactionEndOfBattleSummary(TestData.MakeFaction("Player"))).RequireRight().CapturedEnemies.Count);
  }

  [TestCase]
  public void UnconsciousParticipantsRemainPresentAndAliveInHealthBasedSummaries()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var support = battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 1);
    battle.ApplyDamage(battle.PlayerUnit, 19, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    var player = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    var enemy = battle.Query(new GetFactionEndOfBattleSummary(battle.EnemyFaction)).RequireRight();

    Assert.True(player.CombatantsPresent.SetEquals([battle.PlayerUnit.Combatant, support.Combatant]));
    Assert.True(player.CombatantsWounded.SetEquals([battle.PlayerUnit.Combatant]));
    Assert.Equal(0, player.CombatantsDead.Count);
    Assert.Equal(0, player.DefeatedPerCombatant.Count);
    Assert.True(enemy.CombatantsPresent.SetEquals([battle.EnemyUnit.Combatant]));
    Assert.Equal(0, enemy.CombatantsDead.Count);
    Assert.Equal(0, enemy.CombatantsWounded.Count);
    Assert.Equal(0, enemy.DefeatedPerCombatant.Count);
  }

  [TestCase(TestName = "The summary query fails while the battle has not ended")]
  public void SummaryQueryFailsWhileBattleInProgress()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      playerControlled: true,
      player: new("Alpha", Position: new Vector3I(0, 0, 0)),
      enemy: new("Bandit", Position: new Vector3I(2, 0, 0)));

    var failure = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireLeft();
    Assert.Equal(BattleQueryFailureReason.InvalidBattleState, failure.Reason);
  }

  [TestCase(TestName = "A victory summary tallies kills per combatant and faction-filtered dead and wounded")]
  public void VictorySummaryTalliesKillsDeadAndWounded()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(8, 1, 8),
      hitChanceCalculator: new AlwaysHitCalculator(),
      playerControlled: true,
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle", damage: 10)),
      enemy: new("Bandit1", Health: 10));
    var bravo = battle.Spawn(TestData.MakeCombatant("Bravo", battle.PlayerFaction), new Vector3I(0, 0, 0));
    var charlie = battle.Spawn(TestData.MakeCombatant("Charlie", battle.PlayerFaction), new Vector3I(1, 0, 0));
    var bandit2 = battle.Spawn(TestData.MakeCombatant("Bandit2", battle.EnemyFaction, health: 10), new Vector3I(5, 0, 4));

    battle.ApplyDamage(bravo, 999);
    battle.ApplyDamage(charlie, 1);
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    battle.Attack(battle.PlayerUnit, bandit2);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);

    var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();

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
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(8, 1, 8),
      hitChanceCalculator: new AlwaysHitCalculator(),
      playerControlled: true,
      player: new("Alpha", Health: 5),
      enemy: new("Bandit", Position: new Vector3I(4, 0, 3), Weapon: TestData.MakeWeapon("Shiv", damage: 5)));

    battle.ApplyDamage(battle.EnemyUnit, 1);
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.Attack(battle.EnemyUnit, battle.PlayerUnit);
    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);

    var playerSummary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    Assert.Equal(BattleOutcome.Defeat, playerSummary.Outcome);
    Assert.True(playerSummary.CombatantsDead.SetEquals([battle.PlayerUnit.Combatant]));
    Assert.Equal(0, playerSummary.CombatantsWounded.Count, "The wounded enemy must not appear in the player's summary.");
    Assert.Equal(0, playerSummary.DefeatedPerCombatant.Count);

    var enemySummary = battle.Query(new GetFactionEndOfBattleSummary(battle.EnemyFaction)).RequireRight();
    Assert.Equal(0, enemySummary.CombatantsDead.Count);
    Assert.True(enemySummary.CombatantsWounded.SetEquals([battle.EnemyUnit.Combatant]));
    Assert.Equal(1, enemySummary.DefeatedPerCombatant.Count);
    Assert.True(enemySummary.DefeatedPerCombatant[battle.EnemyUnit.Combatant].AsValueEnumerable().SequenceEqual([battle.PlayerUnit.Combatant]));
  }
}
