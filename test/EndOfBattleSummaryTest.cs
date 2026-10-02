using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
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
      var summary = battle.Query(new GetCompletedBattleQuery()).RequireSome()
        .FactionSummaries[battle.PlayerFaction];
      Assert.Equal(1, summary.CapturedEnemies.Count);
      Assert.True(ReferenceEquals(battle.EnemyUnit.Combatant, summary.CapturedEnemies[0]));
    };

    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Equal(1, endedEvents);
  }

  [TestCase(TestName = "Captured membership and numbers stay frozen after completion")]
  public void CapturedMembershipIsFrozenAtTheFirstEndBattle()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    var completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();
    var captured = battle.EnemyUnit.Combatant;

    // Deliberate campaign-side mutation probes frozen membership; this is not a supported
    // gameplay action. The installed completion is never recomputed, and a fresh submission
    // cannot reopen combat to capture again.
    battle.EnemyUnit.ReceiveDamage(20);
    captured.OwningFaction = battle.PlayerFaction;

    var after = battle.Query(new GetCompletedBattleQuery()).RequireSome();
    Assert.True(ReferenceEquals(completed, after));
    Assert.Equal(1, after.FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
    Assert.True(ReferenceEquals(captured, after.FactionSummaries[battle.PlayerFaction].CapturedEnemies[0]));
    Assert.True(battle.Submit(BattleAction.PassUnit(battle.Alive(battle.PlayerUnit))).IsNone);
  }

  [TestCase(TestName = "Only the designated player summary receives victory captures")]
  public void OnlyTheDesignatedPlayerSummaryReceivesVictoryCaptures()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    CompletedBattle completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();

    Assert.Equal(1, completed.FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
    Assert.Equal(0, completed.FactionSummaries[battle.EnemyFaction].CapturedEnemies.Count);
    Assert.False(completed.FactionSummaries.ContainsKey(TestData.MakeFaction("Player")));
  }

  [TestCase]
  public void UnconsciousParticipantsRemainPresentAndAliveInHealthBasedSummaries()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var support = battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 1);
    battle.ApplyDamage(battle.PlayerUnit, 19, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    CompletedBattle completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();
    var player = completed.FactionSummaries[battle.PlayerFaction];
    var enemy = completed.FactionSummaries[battle.EnemyFaction];

    Assert.True(player.CombatantsPresent.SetEquals([battle.PlayerUnit.Combatant, support.Combatant]));
    Assert.True(player.CombatantsWounded.SetEquals([battle.PlayerUnit.Combatant]));
    Assert.Equal(0, player.CombatantsDead.Count);
    Assert.Equal(0, player.DefeatedPerCombatant.Count);
    Assert.True(enemy.CombatantsPresent.SetEquals([battle.EnemyUnit.Combatant]));
    Assert.Equal(0, enemy.CombatantsDead.Count);
    Assert.Equal(0, enemy.CombatantsWounded.Count);
    Assert.Equal(0, enemy.DefeatedPerCombatant.Count);
  }

  [TestCase(TestName = "Summaries carry per-combatant health reports")]
  public void SummaryCarriesHealthReports()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.PlayerUnit, 3);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    CompletedBattle installed = battle.Query(new GetCompletedBattleQuery()).RequireSome();

    var first = installed.FactionSummaries[battle.PlayerFaction];
    var report = first.HealthByCombatant[battle.PlayerUnit.Combatant];
    Assert.Equal(20, report.MaxHealth);
    Assert.Equal(3L, report.HealthDamageTaken);

    // Issued summaries are copies, and the stored completion is frozen: raw campaign-side
    // mutation — a live max-health negative control, faction reassignment, and extra damage,
    // none of it a supported gameplay action — never rewrites installed reports.
    battle.PlayerUnit.ReceiveDamage(5);
    battle.PlayerUnit.Combatant.OwningFaction = battle.EnemyFaction;
    battle.PlayerUnit.Combatant.GetStat<HealthStat>().BaseValue = 40;
    Assert.Equal(40, battle.PlayerUnit.MaxHealth);
    Assert.Equal(3L, first.HealthByCombatant[battle.PlayerUnit.Combatant].HealthDamageTaken);

    // A fresh read answers the SAME installed completion with its captured numbers,
    // membership, counts, turn, and outcome.
    var again = battle.Query(new GetCompletedBattleQuery()).RequireSome();
    Assert.True(ReferenceEquals(installed, again));
    Assert.Equal(BattleOutcome.Victory, again.Outcome);
    Assert.Equal(1, again.TurnCount);
    Assert.Equal(1, again.Factions[battle.PlayerFaction].Spawned);
    Assert.Equal(0, again.Factions[battle.PlayerFaction].Killed);
    Assert.Equal(1, again.Factions[battle.EnemyFaction].Spawned);
    Assert.Equal(0, again.Factions[battle.EnemyFaction].Killed);
    var frozen = again.FactionSummaries[battle.PlayerFaction];
    Assert.Equal(20, frozen.HealthByCombatant[battle.PlayerUnit.Combatant].MaxHealth);
    Assert.Equal(3L, frozen.HealthByCombatant[battle.PlayerUnit.Combatant].HealthDamageTaken);
    Assert.True(frozen.CombatantsPresent.Contains(battle.PlayerUnit.Combatant));
    Assert.True(frozen.CombatantsWounded.Contains(battle.PlayerUnit.Combatant));
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
    // A second runtime state of the SAME campaign combatant: two distinct runtime killer
    // states project onto one campaign identity, and the frozen ledger must accumulate both
    // victims under that one key instead of colliding on the projected key.
    BattleUnitState reinforcement = battle.Spawn(battle.PlayerUnit.Combatant,
      new Vector3I(5, 0, 3), TestData.MakeWeapon("Rifle", damage: 10));

    battle.ApplyDamage(bravo, 999);
    battle.ApplyDamage(charlie, 1);
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    battle.Attack(reinforcement, bandit2);   // the reinforcement's last kill completes the battle instantly
    CompletedBattle completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();

    var summary = completed.FactionSummaries[battle.PlayerFaction];
    Assert.Equal(battle.PlayerFaction, summary.Faction);
    Assert.Equal(BattleOutcome.Victory, summary.Outcome);
    Assert.Equal(completed.TurnCount, summary.TurnCount);
    Assert.True(summary.CombatantsDead.SetEquals([bravo.Combatant]), "Dead should hold only the fallen player combatant, not enemy dead.");
    Assert.True(summary.CombatantsWounded.SetEquals([charlie.Combatant]), "Wounded should hold only the hurt-but-alive player combatant.");
    Assert.True(summary.CombatantsPresent.SetEquals(
      [battle.PlayerUnit.Combatant, bravo.Combatant, charlie.Combatant]),
      "Present should hold every faction combatant — dead, wounded, and untouched alike.");
    Assert.Equal(1, summary.DefeatedPerCombatant.Count);
    // One shared killer identity: the ledger key is the player unit's own campaign combatant,
    // and both victims accumulate under it in kill order.
    Assert.True(summary.DefeatedPerCombatant.ContainsKey(battle.PlayerUnit.Combatant));
    Assert.True(summary.DefeatedPerCombatant[battle.PlayerUnit.Combatant].AsValueEnumerable().SequenceEqual([battle.EnemyUnit.Combatant, bandit2.Combatant]));
    Assert.Equal(2, completed.Factions[battle.EnemyFaction].Killed);

    CompletedReportsDoNotExposeWritableCollections(completed);
  }

  // Frozen completed reports must not expose writable collection interfaces — outer
  // dictionaries, roster sets, and the kill ledger's nested defeated lists included. An
  // implemented mutable interface must at least report IsReadOnly; a missing cast is fine.
  private static void CompletedReportsDoNotExposeWritableCollections(CompletedBattle completed)
  {
    AssertReadOnly(completed.Factions);
    AssertReadOnly(completed.FactionSummaries);
    foreach (FactionBattleSummary summary in completed.FactionSummaries.Values)
    {
      AssertReadOnly(summary.CombatantsPresent);
      AssertReadOnly(summary.CombatantsDead);
      AssertReadOnly(summary.CombatantsWounded);
      AssertReadOnly(summary.DefeatedPerCombatant);
      AssertReadOnly(summary.HealthByCombatant);
      AssertReadOnly(summary.CapturedEnemies);
      foreach (SysColGeneric.IReadOnlyList<Combatant> defeated in summary.DefeatedPerCombatant.Values)
        AssertReadOnly(defeated);
    }
  }

  private static void AssertReadOnly<K, V>(SysColGeneric.IReadOnlyDictionary<K, V> collection)
  {
    if (collection is SysColGeneric.IDictionary<K, V> mutable)
      Assert.True(mutable.IsReadOnly);
  }

  private static void AssertReadOnly<T>(SysColGeneric.IReadOnlySet<T> collection)
  {
    if (collection is SysColGeneric.ISet<T> mutable)
      Assert.True(mutable.IsReadOnly);
  }

  private static void AssertReadOnly<T>(SysColGeneric.IReadOnlyList<T> collection)
  {
    if (collection is SysColGeneric.IList<T> mutable)
      Assert.True(mutable.IsReadOnly);
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
    CompletedBattle completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();

    var playerSummary = completed.FactionSummaries[battle.PlayerFaction];
    Assert.Equal(BattleOutcome.Defeat, playerSummary.Outcome);
    Assert.True(playerSummary.CombatantsDead.SetEquals([battle.PlayerUnit.Combatant]));
    Assert.Equal(0, playerSummary.CombatantsWounded.Count, "The wounded enemy must not appear in the player's summary.");
    Assert.Equal(0, playerSummary.DefeatedPerCombatant.Count);

    var enemySummary = completed.FactionSummaries[battle.EnemyFaction];
    Assert.Equal(0, enemySummary.CombatantsDead.Count);
    Assert.True(enemySummary.CombatantsWounded.SetEquals([battle.EnemyUnit.Combatant]));
    Assert.Equal(1, enemySummary.DefeatedPerCombatant.Count);
    Assert.True(enemySummary.DefeatedPerCombatant[battle.EnemyUnit.Combatant].AsValueEnumerable().SequenceEqual([battle.PlayerUnit.Combatant]));
  }
}
