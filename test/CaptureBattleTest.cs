using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CaptureBattleTest
{
  [TestCase]
  public void VictorySummaryRetainsTheExactEnemyCombatant()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var combatant = battle.EnemyUnit.Combatant;
    var originalFaction = combatant.OwningFaction;

    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    Assert.Equal(1, summary.CapturedEnemies.Count);
    Assert.True(ReferenceEquals(combatant, summary.CapturedEnemies[0]));
    Assert.True(ReferenceEquals(originalFaction, combatant.OwningFaction));
  }

  [TestCase]
  public void KilledEnemyIsNotCaptured()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Health);

    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(0, battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight().CapturedEnemies.Count);
  }

  [TestCase]
  public void ConsciousEnemyIsNotCapturedDuringObjectiveVictory()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var objective = new FakeObjective(new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(TurnEndedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    battle.Session.AddObjective(battle.PlayerFaction, objective);
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(0, battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight().CapturedEnemies.Count);
  }

  [TestCase]
  public void UnconsciousFriendlyIsNotCapturedWhileConsciousSupportWins()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.Spawn(MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    Assert.Equal(1, summary.CapturedEnemies.Count);
    Assert.True(ReferenceEquals(battle.EnemyUnit.Combatant, summary.CapturedEnemies[0]));
  }

  [TestCase]
  public void PlayerDefeatCapturesNobodyEvenWithAnUnconsciousEnemy()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.Spawn(MakeCombatant("Support", battle.EnemyFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(0, battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight().CapturedEnemies.Count);
  }

  [TestCase]
  public void DrawCapturesNobodyEvenWithAnUnconsciousEnemy()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.Spawn(MakeCombatant("Support", battle.EnemyFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.Session.EndBattle(BattleOutcome.Draw);

    Assert.Equal(0, battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight().CapturedEnemies.Count);
  }

  [TestCase]
  public void NoDesignatedPlayerCapturesNobodyOnVictory()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.Session.EndBattle(BattleOutcome.Victory);

    Assert.Equal(0, battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight().CapturedEnemies.Count);
    Assert.Equal(0, battle.Query(new GetFactionEndOfBattleSummary(battle.EnemyFaction)).RequireRight().CapturedEnemies.Count);
  }

  [TestCase]
  public void VictoryIncludesUnconsciousCombatantsFromEveryOpposingFaction()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    var thirdFaction = MakeFaction("Third");
    var third = battle.Spawn(MakeCombatant("Third hostile", thirdFaction), new Vector3I(0, 0, 0));
    battle.Session.AddObjective(thirdFaction, new EliminateAllOpposingForcesObjectiveData().Instantiate());
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(third, 20, DamageKind.Stun);

    var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    Assert.Equal(2, summary.CapturedEnemies.Count);
    Assert.True(summary.CapturedEnemies.AsValueEnumerable().Any(c => ReferenceEquals(c, third.Combatant)));
    Assert.True(summary.CapturedEnemies.AsValueEnumerable().Any(c => ReferenceEquals(c, battle.EnemyUnit.Combatant)));
  }
}
