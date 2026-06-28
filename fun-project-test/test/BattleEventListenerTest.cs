using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleEventListenerTest
{
  private sealed class RecordingListener : BattleEventListener
  {
    public List<BattleEvent> Received { get; } = [];

    public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
      => Received.Add(battleEvent);
  }

  private sealed record ProbeBattleEvent : BattleEvent;

  private sealed class RaiseProbeOnTurnEndedListener : BattleEventListener
  {
    public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
    {
      if (battleEvent is TurnEndedBattleEvent)
        session.RaiseEvent(new ProbeBattleEvent());
    }
  }

  private sealed partial class CountingTrigger : BattleTrigger
  {
    public int Evaluations { get; private set; }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      if (battleEvent is not ProbeBattleEvent)
        return BattleTriggerResult.NoReaction();

      Evaluations++;
      return BattleTriggerResult.NoReaction();
    }
  }

  private static (BattleSession session, BattleActionExecutor executor, Faction faction) MakeSessionWithUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var executor = new BattleActionExecutor(session);
    return (session, executor, faction);
  }

  [TestCase(TestName = "Listener registered for a concrete event type receives only that event")]
  public void ListenerReceivesOnlyItsConcreteEvent()
  {
    var (session, _, _) = MakeSessionWithUnit();
    var turnStartedListener = new RecordingListener();
    var damagedListener = new RecordingListener();
    session.RegisterListener<TurnStartedBattleEvent>(turnStartedListener);
    session.RegisterListener<UnitDamagedBattleEvent>(damagedListener);

    StartBattle(session);

    Assert.Equal(1, turnStartedListener.Received.Count);
    Assert.True(turnStartedListener.Received.Single() is TurnStartedBattleEvent);
    Assert.Equal(0, damagedListener.Received.Count);
  }

  [TestCase(TestName = "Listener registered for a marker interface receives implementing events")]
  public void ListenerReceivesMarkerInterfaceEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitEventsListener = new RecordingListener();
    session.RegisterListener<IUnitBattleEvent>(unitEventsListener);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    Assert.True(unitEventsListener.Received.OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "Event raised by a listener dispatches after the current event and reaches triggers")]
  public void ListenerRaisedEventDispatchesAfterCurrentEvent()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    session.RegisterListener<TurnEndedBattleEvent>(new RaiseProbeOnTurnEndedListener());
    var trigger = new CountingTrigger();
    executor.RegisterTrigger<ProbeBattleEvent>(trigger);
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    var result = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();

    Assert.True(result.Succeeded);
    int turnEndedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is TurnEndedBattleEvent);
    int probeIndex = raisedEvents.FindIndex(battleEvent => battleEvent is ProbeBattleEvent);
    int turnStartedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is TurnStartedBattleEvent);
    Assert.True(turnEndedIndex >= 0);
    Assert.True(probeIndex > turnEndedIndex);
    Assert.True(turnStartedIndex > probeIndex);
    Assert.Equal(1, trigger.Evaluations);
  }

  private sealed class RaiseThenThrowOnceOnTurnEndedListener : BattleEventListener
  {
    private bool _hasThrown;

    public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
    {
      if (_hasThrown || battleEvent is not TurnEndedBattleEvent)
        return;

      _hasThrown = true;
      session.RaiseEvent(new ProbeBattleEvent());
      throw new InvalidOperationException("Listener failure.");
    }
  }

  [TestCase(TestName = "A throwing listener fails the action and clears undispatched events")]
  public void ThrowingListenerFailsActionAndClearsQueue()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    session.RegisterListener<TurnEndedBattleEvent>(new RaiseThenThrowOnceOnTurnEndedListener());
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    var result = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.False(raisedEvents.OfType<ProbeBattleEvent>().Any());

    var followUp = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();
    Assert.True(followUp.Succeeded);
    Assert.False(raisedEvents.OfType<ProbeBattleEvent>().Any());
  }
}
