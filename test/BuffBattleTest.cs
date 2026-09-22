using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffBattleTest
{
  private static Buff HealthBelowAimBuff() =>
    TestData.MakeBuff(
      "Frenzy",
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);

  [TestCase(TestName = "A buff whose condition already holds activates at spawn, after UnitAdded")]
  public void SpawnEvaluatesBuffs()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var alwaysOn = TestData.MakeBuff(
      "Steady",
      new HealthBelowPercentCondition { Percent = 200f }, // current < 2*max: always true
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(5)] }]);

    var unit = battle.Spawn(
      TestData.MakeCombatant("Alpha", faction, buffs: [alwaysOn]),
      new Vector3I(4, 0, 4));

    Assert.Equal(1, unit.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(alwaysOn, battle.Events.SingleEvent<UnitBuffActivatedBattleEvent>().Buff);
    battle.Events.EventBefore<UnitAddedBattleEvent, UnitBuffActivatedBattleEvent>();
  }

  [TestCase(TestName = "A condition met mid-turn activates at the NEXT turn start and shifts the hit-chance preview")]
  public void ActivationWaitsForTurnStart()
  {
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Weapon: TestData.MakeWeapon("Saber", damage: 5), Buffs: [HealthBelowAimBuff()]));
    var player = battle.PlayerUnit;
    battle.ClearEvents();

    HitChanceBreakdown Preview() => battle.Query(new GetHitChanceForAttack(
      battle.Alive(battle.PlayerUnit), battle.Target(battle.EnemyUnit))).RequireRight();

    battle.ApplyDamage(player, 11); // 9/20: condition now holds
    Assert.Equal(0, player.ActiveBuffs.AsValueEnumerable().Count());  // poll model: nothing until a turn boundary
    Assert.Equal(65, Preview().FinalChance); // open ground, no cover: preview = base aim

    battle.EndFactionTurn(battle.PlayerFaction); // enemy turn starts -> EvaluateAll

    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count()); // fresh during the ENEMY turn (every-turn-start eval)
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    Assert.Equal(75, Preview().FinalChance); // spec test 4: the preview sees the aim buff
    Assert.Equal(1, battle.Events.EventsOf<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());
    // v2 hook order pin: TurnStarted commits BEFORE the buff hook's activation (buff
    // evaluation is fired by the executor after the TurnStarted broadcast), not after.
    battle.Events.EventBefore<TurnStartedBattleEvent, UnitBuffActivatedBattleEvent>();
  }

  [TestCase(TestName = "An AP buff active at the owner's turn start is included in the refreshed action points")]
  public void ApBuffLandsBeforeRefresh()
  {
    var apBuff = TestData.MakeBuff(
      "Adrenaline",
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new ActionPointsStatMod { Modifiers = [StatModifier.Add(2)] }]);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", ActionPoints: 4, Buffs: [apBuff]));
    var player = battle.PlayerUnit;

    battle.EndFactionTurn(battle.PlayerFaction); // enemy turn (buff evaluated: still inactive)
    battle.ApplyDamage(player, 11);               // condition becomes true DURING the enemy turn
    battle.EndFactionTurn(battle.EnemyFaction);  // player turn starts: eval must precede refresh

    // The damage lands after the last evaluation before the player's refresh, so the flip can
    // only happen in the same transition that refreshes AP: if the refresh ran first it would
    // read Max=4 (buff not yet active) and CurrentActionPoints would be 4, failing this assert.
    Assert.Equal(6, player.MaxActionPoints);
    Assert.Equal(6, player.CurrentActionPoints); // the ordering regression this design exists for
  }

  [TestCase(TestName = "A buff deactivates at the next turn start once its condition no longer holds")]
  public void DeactivationOnTurnStart()
  {
    var aura = TestData.MakeBuff(
      "Riposte",
      new AdjacentEnemyCondition(),
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var player = battle.Spawn(
      TestData.MakeCombatant("Alpha", playerFaction, buffs: [aura]),
      new Vector3I(4, 0, 3),
      TestData.MakeWeapon("Saber", damage: 5));
    // Adjacent, killable in one hit; a second enemy keeps the battle running after the kill.
    var adjacentEnemy = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction, health: 5), new Vector3I(4, 0, 4));
    battle.Spawn(TestData.MakeCombatant("Backline", enemyFaction), new Vector3I(0, 0, 0));
    battle.Start();

    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count()); // activated by the battle-start pass

    battle.ClearEvents();
    battle.Attack(player, adjacentEnemy); // kills the adjacent enemy
    battle.EndFactionTurn(playerFaction);          // enemy turn start -> re-evaluate

    Assert.Equal(0, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(aura, battle.Events.SingleEvent<UnitBuffDeactivatedBattleEvent>().Buff);
  }

  [TestCase(TestName = "A negative max-health buff activating at spawn clamps current health down")]
  public void HealthClampOnActivation()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var intimidation = TestData.MakeBuff(
      "Intimidated",
      new AdjacentEnemyCondition(),
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-5)] }]);

    battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 3));
    // Spawns adjacent to the player: its own spawn-time evaluation sees the condition met.
    var enemy = battle.Spawn(
      TestData.MakeCombatant("Hostile", enemyFaction, health: 20, buffs: [intimidation]),
      new Vector3I(4, 0, 4));

    Assert.Equal(15, enemy.MaxHealth);
    Assert.Equal(15, enemy.CurrentHealth);
  }
}
