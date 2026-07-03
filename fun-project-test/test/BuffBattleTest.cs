using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffBattleTest
{
  private static BuffData HealthBelowAimBuff() =>
    MakeBuff(
      "Frenzy",
      new HealthBelowPercentConditionData { Percent = 50f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);

  [TestCase(TestName = "A buff whose condition already holds activates at spawn, after UnitAdded")]
  public void SpawnEvaluatesBuffs()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var recorder = new BattleEventRecorder(session);
    var alwaysOn = MakeBuff(
      "Steady",
      new HealthBelowPercentConditionData { Percent = 200f }, // current < 2*max: always true
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(5)] }]);

    var unit = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, buffs: [alwaysOn]),
      new Vector3I(4, 0, 4));

    Assert.True(unit.State.Buffs[0].IsActive);
    Assert.Equal(alwaysOn, recorder.Single<UnitBuffActivatedBattleEvent>().Buff);
    recorder.AssertCommittedBefore<UnitAddedBattleEvent, UnitBuffActivatedBattleEvent>();
  }

  [TestCase(TestName = "A condition met mid-turn activates at the NEXT turn start and shifts the hit-chance preview")]
  public void ActivationWaitsForTurnStart()
  {
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Weapon: MakeWeapon("Saber", damage: 5), Buffs: [HealthBelowAimBuff()]),
      Enemy = new DuelSide("Hostile"),
    }.Start();
    var player = battle.PlayerUnit.State;
    var recorder = new BattleEventRecorder(battle.Session);
    HitChanceBreakdown Preview() => GetValue(Query(battle.Session,
      new GetHitChanceForAttack(
        battle.PlayerUnit.AliveIn(battle.Session),
        battle.EnemyUnit.AliveIn(battle.Session))));

    ApplyDamage(battle.Session, player, 11); // 9/20: condition now holds
    Assert.False(player.Buffs[0].IsActive);  // poll model: nothing until a turn boundary
    Assert.Equal(65, Preview().FinalChance); // open ground, no cover: preview = base aim

    EndFactionTurn(battle.Executor, battle.PlayerFaction); // enemy turn starts -> EvaluateAll

    Assert.True(player.Buffs[0].IsActive); // fresh during the ENEMY turn (every-turn-start eval)
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    Assert.Equal(75, Preview().FinalChance); // spec test 4: the preview sees the aim buff
    Assert.Equal(1, recorder.OfType<UnitBuffActivatedBattleEvent>().Count());
  }

  [TestCase(TestName = "An AP buff active at the owner's turn start is included in the refreshed action points")]
  public void ApBuffLandsBeforeRefresh()
  {
    var apBuff = MakeBuff(
      "Adrenaline",
      new HealthBelowPercentConditionData { Percent = 50f },
      statMods: [new ActionPointsStatMod { Modifiers = [StatModifier.Add(2)] }]);
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", ActionPoints: 4, Buffs: [apBuff]),
      Enemy = new DuelSide("Hostile"),
    }.Start();
    var player = battle.PlayerUnit.State;

    EndFactionTurn(battle.Executor, battle.PlayerFaction); // enemy turn (buff evaluated: still inactive)
    ApplyDamage(battle.Session, player, 11);               // condition becomes true DURING the enemy turn
    EndFactionTurn(battle.Executor, battle.EnemyFaction);  // player turn starts: eval must precede refresh

    // The damage lands after the last evaluation before the player's refresh, so the flip can
    // only happen in the same transition that refreshes AP: if the refresh ran first it would
    // read Max=4 (buff not yet active) and CurrentActionPoints would be 4, failing this assert.
    Assert.Equal(6, player.MaxActionPoints);
    Assert.Equal(6, player.CurrentActionPoints); // the ordering regression this design exists for
  }

  [TestCase(TestName = "A buff deactivates at the next turn start once its condition no longer holds")]
  public void DeactivationOnTurnStart()
  {
    var aura = MakeBuff(
      "Riposte",
      new AdjacentEnemyConditionData(),
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);
    var playerFaction = MakeFaction("Player");
    var enemyFaction = MakeFaction("Enemy");
    var session = MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var player = SpawnUnit(
      session,
      MakeCombatant("Alpha", playerFaction, buffs: [aura]),
      new Vector3I(4, 0, 3),
      MakeWeapon("Saber", damage: 5));
    // Adjacent, killable in one hit; a second enemy keeps the battle running after the kill.
    var adjacentEnemy = SpawnUnit(session, MakeCombatant("Hostile", enemyFaction, health: 5), new Vector3I(4, 0, 4));
    SpawnUnit(session, MakeCombatant("Backline", enemyFaction), new Vector3I(0, 0, 0));
    StartBattle(session);

    Assert.True(player.State.Buffs[0].IsActive); // activated by the battle-start pass

    var executor = new BattleActionExecutor(session);
    var recorder = new BattleEventRecorder(session);
    Attack(executor, player.State, adjacentEnemy.State); // kills the adjacent enemy
    EndFactionTurn(executor, playerFaction);             // enemy turn start -> re-evaluate

    Assert.False(player.State.Buffs[0].IsActive);
    Assert.Equal(aura, recorder.Single<UnitBuffDeactivatedBattleEvent>().Buff);
  }

  [TestCase(TestName = "A negative max-health buff activating at spawn clamps current health down")]
  public void HealthClampOnActivation()
  {
    var playerFaction = MakeFaction("Player");
    var enemyFaction = MakeFaction("Enemy");
    var session = MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var intimidation = MakeBuff(
      "Intimidated",
      new AdjacentEnemyConditionData(),
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-5)] }]);

    SpawnUnit(session, MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 3));
    // Spawns adjacent to the player: its own spawn-time evaluation sees the condition met.
    var enemy = SpawnUnit(
      session,
      MakeCombatant("Hostile", enemyFaction, health: 20, buffs: [intimidation]),
      new Vector3I(4, 0, 4));

    Assert.Equal(15, enemy.State.MaxHealth);
    Assert.Equal(15, enemy.State.CurrentHealth);
  }
}
