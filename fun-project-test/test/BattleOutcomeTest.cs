using FunProject.Battle;
using FunProject.Core;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class BattleOutcomeTest
{
  [TestCase(TestName = "A session exposes its declared player faction and no outcome until it ends")]
  public void SessionExposesPlayerFactionAndNoOutcomeUntilEnded()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));

    Assert.Equal(player, session.PlayerFaction.RequireSome());
    Assert.True(session.Outcome.IsNone);
  }

  [TestCase(TestName = "A wiped player loses immediately")]
  public void PlayerWipeLosesImmediately()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      PlayerControlled = true,
      Player = new("Alpha", Position: new Vector3I(0, 0, 0)),
      Enemy = new("Bandit", Position: new Vector3I(2, 0, 0)),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    ApplyDamage(battle.Session, battle.PlayerUnit, 999);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
    var ended = recorder.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Defeat, ended.Outcome);
  }

  [TestCase(TestName = "Player becoming the sole surviving side resolves to Victory at the end of the turn")]
  public void PlayerSoleSurvivorResolvesToVictoryAtTurnEnd()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      PlayerControlled = true,
      Player = new("Alpha", Position: new Vector3I(0, 0, 0)),
      Enemy = new("Bandit", Position: new Vector3I(2, 0, 0)),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    ApplyDamage(battle.Session, battle.EnemyUnit, 999);
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.True(battle.Session.Outcome.IsNone);

    AdvanceTurn(battle.Session);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Victory, battle.Session.Outcome.RequireSome());
    var ended = recorder.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Victory, ended.Outcome);
  }

  [TestCase(TestName = "Without a player faction a sole survivor keeps playing and the battle does not end")]
  public void NoPlayerSoleSurvivorKeepsPlaying()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      Player = new("A1", Position: new Vector3I(0, 0, 0)),
      Enemy = new("B1", Position: new Vector3I(2, 0, 0)),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    ApplyDamage(battle.Session, battle.EnemyUnit, 999);
    AdvanceTurn(battle.Session);

    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.True(battle.Session.Outcome.IsNone);
    Assert.False(recorder.OfType<SessionEndedBattleEvent>().Any());

    // A lone survivor keeps taking turns across the round boundary — the path
    // adjacent to StartNextRound's impossible-state guard — without ending.
    AdvanceTurn(battle.Session);

    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(3, battle.Session.TurnNumber);
    Assert.True(battle.Session.Outcome.IsNone);
    Assert.False(recorder.OfType<SessionEndedBattleEvent>().Any());
  }

  [TestCase(TestName = "Without a player faction total annihilation resolves to Draw at the end of the turn")]
  public void NoPlayerTotalAnnihilationResolvesToDraw()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      Player = new("A1", Position: new Vector3I(0, 0, 0)),
      Enemy = new("B1", Position: new Vector3I(2, 0, 0)),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    ApplyDamage(battle.Session, battle.EnemyUnit, 999);
    ApplyDamage(battle.Session, battle.PlayerUnit, 999);
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);

    AdvanceTurn(battle.Session);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Draw, battle.Session.Outcome.RequireSome());
    var ended = recorder.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Draw, ended.Outcome);
  }

  [TestCase(TestName = "An end-of-turn DoT kill that wipes the player resolves to Defeat without throwing")]
  public void EndOfTurnDotKillResolvesOutcome()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      PlayerControlled = true,
      Player = new("Alpha", Position: new Vector3I(0, 0, 0), Health: 2),
      Enemy = new("Bandit", Position: new Vector3I(2, 0, 0)),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    battle.PlayerUnit.State.ApplyStatusEffect(new DamageOverTimeStatusSpecData
    {
      Name = "Burn",
      DurationTurns = 2,
      TickDamage = 5,
      TickElement = Element.Kinetic,
    });

    AdvanceTurn(battle.Session);

    Assert.Equal(BattlePhase.Ended, battle.Session.Phase);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
    var ended = recorder.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Defeat, ended.Outcome);
  }
}
