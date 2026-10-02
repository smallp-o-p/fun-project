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

    var summary = battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction];
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
    Assert.Equal(0, battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
  }

  [TestCase]
  public void ConsciousEnemyIsNotCapturedDuringObjectiveVictory()
  {
    using var battle = BattleFixture.Duel(playerControlled: true, start: false);
    var objective = new FakeObjective(new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(TurnEndedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    battle.AddObjective(battle.PlayerFaction, objective);
    battle.Start();
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(0, battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
  }

  [TestCase]
  public void UnconsciousFriendlyIsNotCapturedWhileConsciousSupportWins()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.Spawn(MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    var summary = battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction];
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
    Assert.Equal(0, battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
  }

  [TestCase]
  public void DrawCapturesNobodyEvenWithAnUnconsciousEnemy()
  {
    // The designated player draws by objective at the turn end while an unconscious enemy
    // is present at the capture boundary: capture membership is victory-only.
    using var battle = BattleFixture.Duel(playerControlled: true, start: false);
    battle.AddObjective(battle.PlayerFaction, new FakeObjective(new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(TurnEndedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Draw },
    }));
    battle.Start();
    Assert.True(battle.Query(new GetPlayerFactionQuery()).IsSome);

    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    Assert.True(battle.EnemyUnit.IsUnconscious);
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(BattleOutcome.Draw, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(0, battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
  }

  [TestCase]
  public void NoDesignatedPlayerCapturesNobodyOnVictory()
  {
    // The survive objective completes on round 2 — the unconscious enemy body is eligible
    // at that boundary, but without a designated player nobody is ever captured.
    using var battle = BattleFixture.Duel(start: false);
    battle.AddObjective(battle.PlayerFaction, new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 2,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    battle.Start();

    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    Assert.True(battle.EnemyUnit.IsUnconscious);
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.Equal(0, battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction].CapturedEnemies.Count);
    Assert.Equal(0, battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.EnemyFaction].CapturedEnemies.Count);
  }

  [TestCase]
  public void VictoryIncludesUnconsciousCombatantsFromEveryOpposingFaction()
  {
    using var battle = BattleFixture.Duel(playerControlled: true, start: false);
    var thirdFaction = MakeFaction("Third");
    var third = battle.Spawn(MakeCombatant("Third hostile", thirdFaction), new Vector3I(0, 0, 0));
    battle.AddObjective(thirdFaction, new EliminateAllOpposingForcesObjectiveData().Instantiate());
    battle.Start();
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(third, 20, DamageKind.Stun);

    var summary = battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[battle.PlayerFaction];
    Assert.Equal(2, summary.CapturedEnemies.Count);
    Assert.True(summary.CapturedEnemies.AsValueEnumerable().Any(c => ReferenceEquals(c, third.Combatant)));
    Assert.True(summary.CapturedEnemies.AsValueEnumerable().Any(c => ReferenceEquals(c, battle.EnemyUnit.Combatant)));
  }
}
