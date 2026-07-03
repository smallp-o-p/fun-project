using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleEventSignalHandlerTest
{
  [TestCase(TestName = "Adapter emits presentation event for committed runtime event")]
  public void AdapterEmitsPresentationEventForCommittedRuntimeEvent()
  {
    using var fixture = new SignalHandlerFixture(new Vector3I(3, 1, 3));
    var committedEvents = new List<BattleEvent>();
    var events = new List<BattleEventAdapter>();

    fixture.Runtime.BattleEventCommitted += committedEvents.Add;
    fixture.Adapter.PresentationEventCommitted += events.Add;
    fixture.Adapter.Bind(fixture.Runtime);

    BattleActionResult result = fixture.Runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", fixture.Faction),
      fixture.Session.Board.At(1, 0, 1))).RequireSingleResult();

    Assert.True(result.Succeeded);
    BattleEvent committedEvent = committedEvents.Single();
    BattleEventAdapter presentationEvent = events.Single();
    Assert.True(ReferenceEquals(committedEvent, presentationEvent.BattleEvent));
    Assert.Equal(nameof(UnitAddedBattleEvent), presentationEvent.EventName);
    Assert.True(presentationEvent.BattleEvent is UnitAddedBattleEvent);

    var addedEvent = (UnitAddedBattleEvent)presentationEvent.BattleEvent;
    Assert.Equal("Alpha", addedEvent.Unit.Combatant.Name);
    Assert.Equal("Player", addedEvent.Unit.Side.Name);
    Assert.Equal(new Vector3I(1, 0, 1), addedEvent.Position.Raw);
  }

  [TestCase(TestName = "Adapter emits action lifecycle signals")]
  public void AdapterEmitsActionLifecycleSignals()
  {
    using var fixture = new SignalHandlerFixture(new Vector3I(3, 1, 3));
    var startedActions = new List<string>();
    var completedResults = new List<BattleActionResultAdapter>();

    fixture.Adapter.ActionStarted += startedActions.Add;
    fixture.Adapter.ActionCompleted += completedResults.Add;
    fixture.Adapter.Bind(fixture.Runtime);

    fixture.Runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", fixture.Faction),
      fixture.Session.Board.At(1, 0, 1))).RequireSingleResult();

    Assert.Equal(1, startedActions.Count);
    Assert.Equal("spawn_unit", startedActions[0]);
    BattleActionResultAdapter result = completedResults.Single();
    Assert.Equal("spawn_unit", result.ActionId);
    Assert.True(result.Succeeded);
    Assert.Equal("Alpha", result.AffectedUnit.RequireSome().Combatant.Name);
  }

  [TestCase(TestName = "Unbind stops forwarding runtime events")]
  public void UnbindStopsForwardingRuntimeEvents()
  {
    using var fixture = new SignalHandlerFixture(new Vector3I(3, 1, 3));
    var events = new List<BattleEventAdapter>();

    fixture.Adapter.PresentationEventCommitted += events.Add;
    fixture.Adapter.Bind(fixture.Runtime);

    fixture.Adapter.Unbind();
    fixture.Runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", fixture.Faction),
      fixture.Session.Board.At(1, 0, 1))).RequireSingleResult();

    Assert.False(fixture.Adapter.IsBound);
    Assert.Equal(0, events.Count);
  }

  [TestCase(TestName = "Rebinding unsubscribes previous runtime")]
  public void RebindingUnsubscribesPreviousRuntime()
  {
    using var fixture = new SignalHandlerFixture(new Vector3I(3, 1, 3));
    var secondSession = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [fixture.Faction]);
    var secondRuntime = new BattleRuntime(secondSession);
    var events = new List<BattleEventAdapter>();

    fixture.Adapter.PresentationEventCommitted += events.Add;

    fixture.Adapter.Bind(fixture.Runtime);
    fixture.Adapter.Bind(secondRuntime);
    fixture.Runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Ignored", fixture.Faction),
      fixture.Session.Board.At(0, 0, 0))).RequireSingleResult();
    secondRuntime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", fixture.Faction),
      secondSession.Board.At(1, 0, 1))).RequireSingleResult();

    var addedEvent = (UnitAddedBattleEvent)events.Single().BattleEvent;
    Assert.Equal("Alpha", addedEvent.Unit.Combatant.Name);
  }

  [TestCase(TestName = "Free unsubscribes runtime events")]
  public void FreeUnsubscribesRuntimeEvents()
  {
    var fixture = new SignalHandlerFixture(new Vector3I(3, 1, 3));
    var events = new List<BattleEventAdapter>();
    fixture.Adapter.PresentationEventCommitted += events.Add;
    fixture.Adapter.Bind(fixture.Runtime);

    fixture.Adapter.Free();
    fixture.Runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", fixture.Faction),
      fixture.Session.Board.At(1, 0, 1))).RequireSingleResult();

    Assert.Equal(0, events.Count);
  }

  [TestCase(TestName = "Killed unit presentation event exposes original death event")]
  public void KilledUnitPresentationEventExposesOriginalDeathEvent()
  {
    using var fixture = new SignalHandlerFixture(new Vector3I(3, 1, 3));
    var events = new List<BattleEventAdapter>();

    fixture.Adapter.PresentationEventCommitted += events.Add;
    fixture.Adapter.Bind(fixture.Runtime);
    var unit = SpawnUnit(
      fixture.Runtime,
      BattleTestFactory.MakeCombatant("Alpha", fixture.Faction, health: 3),
      new Vector3I(1, 0, 1));
    EnsureEveryFactionHasObjective(fixture.Session);
    StartBattle(fixture.Runtime);

    fixture.Runtime.ExecuteAction(BattleAction.ApplyDamage(unit.State, 3)).RequireSingleResult();

    var killedEvent = events
      .Select(presentationEvent => presentationEvent.BattleEvent)
      .OfType<UnitKilledBattleEvent>()
      .Single();
    Assert.Equal(new Vector3I(1, 0, 1), killedEvent.Position.Raw);
    Assert.False(killedEvent.Unit.IsAlive);
  }

  private sealed class SignalHandlerFixture : System.IDisposable
  {
    public Faction Faction { get; }
    public BattleSession Session { get; }
    public BattleRuntime Runtime { get; }
    public BattleEventSignalHandler Adapter { get; }

    public SignalHandlerFixture(Vector3I dimensions)
    {
      Faction = BattleTestFactory.MakeFaction("Player");
      Session = BattleTestFactory.MakeSession(dimensions, [Faction]);
      Runtime = new BattleRuntime(Session);
      Adapter = new BattleEventSignalHandler();
    }

    public void Dispose() => Adapter.Free();
  }
}
