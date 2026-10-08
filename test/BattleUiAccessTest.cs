using System;
using System.Collections.Generic;
using FunProject.Battle;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleUiAccessTest
{
  [TestCase]
  public void HeldWorkerDoesNotBlockPlaybackOrFrameInput()
  {
    using var battle = BattleFixture.UiBattle();
    var ui = battle.Ui;
    var director = battle.AttachDirector(new ImmediateDirector());
    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)]);
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      Assert.Equal(InputGate.RuntimeBusy, ui.Gate);
      Assert.False(ui.TrySelectAt(new Vector3I(4, 0, 2)));
      Assert.False(ui.Confirm());
      while (director.Busy) director.Tick();
      Assert.True(director.Played > 0);
      ui.TickDeferredRefresh();
    }
    Assert.Equal(InputGate.Open, ui.Gate);
  }

  private sealed partial class ImmediateDirector : EventPlaybackDirector
  {
    internal int Played;
    protected override void PlayStep(BattleEvent evt, Action done) { Played++; done(); }
    protected override void PlayMoveStep(IReadOnlyList<UnitMovedBattleEvent> run, Action done) { Played++; done(); }
  }

  [TestCase]
  public void BusyReadKeepsRenderedReadouts()
  {
    using var battle = BattleFixture.UiBattle();
    var ui = battle.Ui;
    ui.TrySelectAt(new Vector3I(4, 0, 1));
    var units = ui.UnitReadouts; var actions = ui.ActionReadouts;
    Assert.True(units.Count > 0);
    Assert.True(actions.Count > 0);
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      ui.TickDeferredRefresh(); ui.Cancel(); ui.BeginAction(ui.ActionOptions[0]);
      Assert.True(ReferenceEquals(units, ui.UnitReadouts));
      Assert.True(ReferenceEquals(actions, ui.ActionReadouts));
      Assert.True(BattleHud.FormatUnitReadout(units[0]).Contains("HP"));
    }
  }

  [TestCase]
  public void LatestHoverRetriesAfterRelease()
  {
    using var battle = BattleFixture.UiBattle();
    var ui = battle.Ui;
    ui.TrySelectAt(new Vector3I(4, 0, 1));
    ui.BeginAction(ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    ui.PreviewAt(new Vector3I(4, 0, 2));
    var before = ui.LastPreview;
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      ui.PreviewAt(new Vector3I(4, 0, 3)); ui.PreviewAt(new Vector3I(5, 0, 2));
      ui.TickDeferredRefresh();
      Assert.Equal(before, ui.LastPreview);
    }
    ui.TickDeferredRefresh();
    Assert.Equal(new Vector3I(5, 0, 2), ((PathPreview)ui.LastPreview.RequireSome()).Path[^1]);
  }

  [TestCase]
  public void StaleTargetIsRebuiltAtSubmission()
  {
    using var battle = BattleFixture.UiBattle();
    var targeter = new MoveTargeting(battle.Runtime, battle.PlayerUnit);
    battle.Read(() => { targeter.Begin(); targeter.Preview(new Vector3I(4, 0, 3)); });
    battle.Mutate(() => battle.Board.SetTileWalkable(battle.At(4, 0, 2), false));
    battle.Runtime.TryExecuteAction(_ =>
    {
      return targeter.Build(new Vector3I(4, 0, 3));
    }).RequireRight();
    Assert.Equal(new Vector3I(4, 0, 3), battle.Alive(battle.PlayerUnit).Position.Raw);
    Assert.False(battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable()
      .Any(evt => evt.Position.Raw == new Vector3I(4, 0, 2)));
  }

  [TestCase]
  public void ClosedUiDiscardsDeferredWork()
  {
    using var battle = BattleFixture.UiBattle();
    var ui = battle.Ui;
    int submissions = 0; battle.Runtime.ActionStarted += _ => submissions++;
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      ui.PreviewAt(Vector3I.Zero);
      battle.Runtime.RequestClose();
      ui.TickDeferredRefresh();
      Assert.Equal(InputGate.RuntimeClosed, ui.Gate);
    }
    ui.TickDeferredRefresh();
    Assert.False(ui.Confirm());
    Assert.Equal(0, submissions);
  }
}
