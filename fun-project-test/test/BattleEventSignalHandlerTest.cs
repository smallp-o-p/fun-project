using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleEventSignalHandlerTest
{
  [TestCase(TestName = "Adapter emits presentation event for committed runtime event")]
  public void AdapterEmitsPresentationEventForCommittedRuntimeEvent()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var adapter = new BattleEventSignalHandler();
    var committedEvents = new List<BattleEvent>();
    var events = new List<BattleEventAdapter>();

    try
    {
      runtime.BattleEventCommitted += committedEvents.Add;
      adapter.PresentationEventCommitted += events.Add;
      adapter.Bind(runtime);

      BattleActionResult result = runtime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", faction),
        new Vector3I(1, 0, 1))).RequireSingleResult();

      Assert.True(result.Succeeded);
      BattleEvent committedEvent = committedEvents.Single();
      BattleEventAdapter presentationEvent = events.Single();
      Assert.True(ReferenceEquals(committedEvent, presentationEvent.BattleEvent));
      Assert.Equal("unit_added", presentationEvent.EventName);
      Assert.Equal("Alpha entered the battle at (1, 0, 1).", presentationEvent.Message);
      Assert.True(presentationEvent.BattleEvent is UnitAddedBattleEvent);

      var addedEvent = (UnitAddedBattleEvent)presentationEvent.BattleEvent;
      Assert.Equal("Alpha", addedEvent.Unit.Combatant.Name);
      Assert.Equal("Player", addedEvent.Unit.Side.Name);
      Assert.Equal(new Vector3I(1, 0, 1), addedEvent.Position.Raw);
    }
    finally
    {
      adapter.Free();
    }
  }

  [TestCase(TestName = "Adapter emits action lifecycle signals")]
  public void AdapterEmitsActionLifecycleSignals()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var adapter = new BattleEventSignalHandler();
    var startedActions = new List<string>();
    var completedResults = new List<BattleActionResultAdapter>();

    try
    {
      adapter.ActionStarted += startedActions.Add;
      adapter.ActionCompleted += completedResults.Add;
      adapter.Bind(runtime);

      runtime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", faction),
        new Vector3I(1, 0, 1))).RequireSingleResult();

      Assert.Equal(1, startedActions.Count);
      Assert.Equal("spawn_unit", startedActions[0]);
      BattleActionResultAdapter result = completedResults.Single();
      Assert.Equal("spawn_unit", result.ActionId);
      Assert.True(result.Succeeded);
      Assert.Equal("Alpha", result.AffectedUnit.RequireSome().Combatant.Name);
    }
    finally
    {
      adapter.Free();
    }
  }

  [TestCase(TestName = "Unbind stops forwarding runtime events")]
  public void UnbindStopsForwardingRuntimeEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var adapter = new BattleEventSignalHandler();
    var events = new List<BattleEventAdapter>();

    try
    {
      adapter.PresentationEventCommitted += events.Add;
      adapter.Bind(runtime);

      adapter.Unbind();
      runtime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", faction),
        new Vector3I(1, 0, 1))).RequireSingleResult();

      Assert.False(adapter.IsBound);
      Assert.Equal(0, events.Count);
    }
    finally
    {
      adapter.Free();
    }
  }

  [TestCase(TestName = "Rebinding unsubscribes previous runtime")]
  public void RebindingUnsubscribesPreviousRuntime()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var firstSession = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var secondSession = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var firstRuntime = new BattleRuntime(firstSession);
    var secondRuntime = new BattleRuntime(secondSession);
    var adapter = new BattleEventSignalHandler();
    var events = new List<BattleEventAdapter>();

    try
    {
      adapter.PresentationEventCommitted += events.Add;

      adapter.Bind(firstRuntime);
      adapter.Bind(secondRuntime);
      firstRuntime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Ignored", faction),
        new Vector3I(0, 0, 0))).RequireSingleResult();
      secondRuntime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", faction),
        new Vector3I(1, 0, 1))).RequireSingleResult();

      var addedEvent = (UnitAddedBattleEvent)events.Single().BattleEvent;
      Assert.Equal("Alpha", addedEvent.Unit.Combatant.Name);
    }
    finally
    {
      adapter.Free();
    }
  }

  [TestCase(TestName = "Free unsubscribes runtime events")]
  public void FreeUnsubscribesRuntimeEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var adapter = new BattleEventSignalHandler();
    var events = new List<BattleEventAdapter>();
    adapter.PresentationEventCommitted += events.Add;
    adapter.Bind(runtime);

    adapter.Free();
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      new Vector3I(1, 0, 1))).RequireSingleResult();

    Assert.Equal(0, events.Count);
  }

  [TestCase(TestName = "Killed unit presentation event exposes original death event")]
  public void KilledUnitPresentationEventExposesOriginalDeathEvent()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var adapter = new BattleEventSignalHandler();
    var events = new List<BattleEventAdapter>();

    try
    {
      adapter.PresentationEventCommitted += events.Add;
      adapter.Bind(runtime);
      var unit = SpawnUnit(
        runtime,
        BattleTestFactory.MakeCombatant("Alpha", faction, health: 3),
        new Vector3I(1, 0, 1));
      StartBattle(runtime);

      runtime.ExecuteAction(BattleAction.ApplyDamage(unit.State, 3)).RequireSingleResult();

      var killedEvent = events
        .Select(presentationEvent => presentationEvent.BattleEvent)
        .OfType<UnitKilledBattleEvent>()
        .Single();
      Assert.Equal(new Vector3I(1, 0, 1), killedEvent.Position.Raw);
      Assert.False(killedEvent.Unit.IsAlive);
    }
    finally
    {
      adapter.Free();
    }
  }

}
