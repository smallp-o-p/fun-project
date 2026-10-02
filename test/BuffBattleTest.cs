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

  private sealed partial class ActiveOnRound : BuffCondition
  {
    public int Round { get; set; }

    internal override bool IsMet(BattleReadContext context, BattleUnitState unit) =>
      context.RunningSession.Match(session => session.RoundNumber == Round, () => false);
  }

  // Supported-condition boundary: reads the ordinary visibility query for an authored tile.
  private sealed partial class TileVisibleCondition : BuffCondition
  {
    public Vector3I Tile { get; set; }

    internal override bool IsMet(BattleReadContext context, BattleUnitState unit) =>
      context.Query(new IsTileVisibleToFaction(
        unit.Side, context.State.Board.ValidatePoint(Tile).RequireSome()));
  }

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
    // Initially unarmed terminal objective observing the turn start: the round-2 player
    // turn both refreshes the owning faction's AP and, once armed, ends the battle there.
    var terminal = new FakeObjective(new FakeObjectiveData
    {
      Observe = typeof(TurnStartedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    using var battle = BattleFixture.Duel(
      player: new("Alpha", ActionPoints: 4, Buffs: [apBuff]), start: false);
    battle.AddObjective(battle.PlayerFaction, terminal);
    battle.Start();
    var player = battle.PlayerUnit;
    int liveApAtSessionEnd = -1;
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is SessionEndedBattleEvent)
        liveApAtSessionEnd = player.CurrentActionPoints;
    });

    battle.EndFactionTurn(battle.PlayerFaction); // enemy turn (buff evaluated: still inactive)
    battle.ApplyDamage(player, 11);               // condition becomes true DURING the enemy turn
    terminal.Complete = true;                     // arm the directive for the next turn start
    battle.EndFactionTurn(battle.EnemyFaction);  // player turn starts: eval must precede refresh

    // The damage lands after the last evaluation before the player's refresh, so the flip can
    // only happen in the same transition that refreshes AP: if the refresh ran first it would
    // read Max=4 (buff not yet active) and CurrentActionPoints would be 4, failing this assert.
    Assert.Equal(6, player.MaxActionPoints);
    Assert.Equal(6, player.CurrentActionPoints); // the ordering regression this design exists for
    // The terminal directive on that same turn start did not truncate the refresh: the end
    // callback observes the fully refreshed AP, and the battle ends without a next turn.
    Assert.Equal(6, liveApAtSessionEnd);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
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

  [TestCase(TestName = "Buff vision activation and deactivation refresh visibility before their events broadcast")]
  public void VisionBuffRefreshesBeforeBroadcast()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    // EagleEye raises vision 1 -> 3 for round 2 only: the tile (and the watcher on it) two
    // cells away flips into and back out of sight at the round-2 and round-3 turn starts.
    // KeenEye's supported condition reads that refreshed visibility in the same passes and
    // gates its aim contribution on it — grant order matters, both flips land in one pass.
    var eagleEye = TestData.MakeBuff(
      "EagleEye",
      new ActiveOnRound { Round = 2 },
      statMods: [new VisionStatMod { Modifiers = [StatModifier.Add(2)] }]);
    var keenEye = TestData.MakeBuff(
      "KeenEye",
      new TileVisibleCondition { Tile = new Vector3I(2, 0, 2) },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);
    using var battle = new BattleFixture(new Vector3I(6, 1, 3), [playerFaction, enemyFaction]);
    // Lone player observer: no conscious teammate already covers the far tile.
    var observer = battle.Spawn(
      TestData.MakeCombatant("Scout", playerFaction, vision: 1, aim: 65, buffs: [eagleEye, keenEye]), new Vector3I(2, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Watcher", enemyFaction, vision: 1), new Vector3I(2, 0, 2));
    BattleBoardState.ValidatedPoint tile = battle.At(new Vector3I(2, 0, 2));

    battle.Start();
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, tile)));
    Assert.False(battle.Query(new HasFactionExploredTile(playerFaction, tile)));
    battle.ClearEvents();

    bool sawActivation = false;
    bool sawDeactivation = false;
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is UnitBuffActivatedBattleEvent activated && ReferenceEquals(activated.Unit, observer))
      {
        sawActivation = true;
        Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, tile)));
        Assert.True(battle.Query(new HasFactionExploredTile(playerFaction, tile)));
        Assert.True(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
      }

      if (battleEvent is UnitBuffDeactivatedBattleEvent deactivated && ReferenceEquals(deactivated.Unit, observer))
      {
        sawDeactivation = true;
        Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, tile)));
        Assert.True(battle.Query(new HasFactionExploredTile(playerFaction, tile)));
      }
    });

    battle.AdvanceTurn(); // round 1: enemy turn, both grants still inactive
    battle.AdvanceTurn(); // round 2: the scout's turn starts and both grants activate in one pass

    Assert.True(sawActivation);
    Assert.Equal(2, battle.Events.EventsOf<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(2, observer.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(75f, observer.EffectiveStat<AimStat>()); // KeenEye's gated contribution is live
    battle.Events.EventBefore<UnitBuffActivatedBattleEvent, UnitSpottedBattleEvent>();

    battle.AdvanceTurn(); // round 2: enemy turn, both grants stay active
    battle.AdvanceTurn(); // round 3: the scout's turn starts and both grants deactivate in one pass

    Assert.True(sawDeactivation);
    Assert.Equal(2, battle.Events.EventsOf<UnitBuffDeactivatedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(0, observer.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(65f, observer.EffectiveStat<AimStat>());
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, tile)));
    Assert.True(battle.Query(new HasFactionExploredTile(playerFaction, tile)));
    Assert.False(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
    Assert.Equal(1, MatchingSpottings(battle, observer, target));
  }

  private static int MatchingSpottings(BattleFixture battle, BattleUnitState observer, BattleUnitState target) =>
    battle.Events.EventsOf<UnitSpottedBattleEvent>()
      .AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target));

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
