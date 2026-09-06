using FunProject.Battle;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleUiControllerTest
{
  // Scenario: BattleFixture.UiBattle() — 8x1x8, Hero (rifle, damage 10) at (4,0,1), Goon
  // (10 health) at (4,0,4), surviving Support at (7,0,7), AlwaysHit, player configured.
  // battle.Ui lazily mints the single controller bound to that runtime and battle.Busy.

  [TestCase(TestName = "Selecting a friendly unit selects it and loads its verbs")]
  public void SelectingFriendlyUnitLoadsVerbs()
  {
    using var battle = BattleFixture.UiBattle();
    Assert.Equal(UiState.Unselected, battle.Ui.State);
    Assert.Equal(InputGate.Open, battle.Ui.Gate);

    Assert.True(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));

    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
    Assert.Equal(battle.PlayerUnit, battle.Ui.SelectedUnit.RequireSome());
    Assert.True(battle.Ui.ActionOptions.Count > 0);
  }

  [TestCase(TestName = "Clicking enemy or empty tiles does not select")]
  public void ClickingEnemyOrEmptyTilesDoesNotSelect()
  {
    using var battle = BattleFixture.UiBattle();
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 4))); // enemy tile
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(0, 0, 0))); // empty tile
    Assert.Equal(UiState.Unselected, battle.Ui.State);
  }

  [TestCase(TestName = "Cancel deselects; StateChanged fires only on transitions")]
  public void CancelDeselectsAndStateChangedFiresOnlyOnTransitions()
  {
    using var battle = BattleFixture.UiBattle();
    int changes = 0;
    battle.Ui.StateChanged += () => changes++;

    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)); // re-select same unit: no transition
    Assert.Equal(1, changes);

    battle.Ui.Cancel();
    Assert.Equal(UiState.Unselected, battle.Ui.State);
    Assert.True(battle.Ui.SelectedUnit.IsNone);
    Assert.Equal(2, changes);
  }

  [TestCase(TestName = "A foreign submission that kills the selected unit deselects")]
  public void ForeignKillDeselects()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(battle.PlayerUnit).RequireSome(), 999));

    Assert.Equal(UiState.Unselected, battle.Ui.State);
    Assert.True(battle.Ui.SelectedUnit.IsNone);
    Assert.Equal(0, battle.Ui.ActionOptions.Count);
  }

  [TestCase(TestName = "A submission that ends the battle enters terminal BattleOver")]
  public void BattleEndEntersTerminalBattleOver()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(battle.EnemyUnit).RequireSome(), 999));
    battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.PlayerFaction));

    Assert.Equal(BattlePhase.Ended, battle.Runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(UiState.BattleOver, battle.Ui.State);
    Assert.True(battle.Ui.SelectedUnit.IsNone);

    // Terminal: further intents are no-ops, gate stays meaningless-but-closed.
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    Assert.Equal(UiState.BattleOver, battle.Ui.State);
  }

  [TestCase(TestName = "BeginAction enters targeting with candidates; invalid clicks no-op")]
  public void BeginActionEntersTargetingWithCandidates()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    MoveActionOption move = battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single();

    battle.Ui.BeginAction(move);

    Assert.Equal(UiState.Targeting, battle.Ui.State);
    Assert.True(battle.Ui.CandidateCells.Count > 0);

    // A tile outside the candidate set is a no-op, still targeting.
    Assert.False(battle.Ui.ClickTile(new Vector3I(0, 0, 6)));
    Assert.Equal(UiState.Targeting, battle.Ui.State);
  }

  [TestCase(TestName = "Move locks then confirms; commit exits targeting via the signal")]
  public void MoveLocksThenConfirmsAndCommits()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());

    Assert.True(battle.Ui.ClickTile(new Vector3I(4, 0, 2))); // Confirm mode: locks
    Assert.Equal(UiState.TargetingLocked, battle.Ui.State);
    Assert.Equal(new Vector3I(4, 0, 2), battle.Ui.PendingTarget.RequireSome());

    Assert.True(battle.Ui.Confirm()); // submits; OnActionResult exits targeting synchronously

    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
    Assert.Equal(0, battle.Ui.CandidateCells.Count);
    var board = battle.Runtime.TryGetTile(new Vector3I(4, 0, 2)).RequireSome();
    Assert.Equal(
      battle.PlayerUnit,
      battle.Runtime.Query(new GetUnitAtTile(board)).RequireSome());
  }

  [TestCase(TestName = "Re-clicking a different tile re-targets; same tile commits")]
  public void LockedRetargetThenSameTileCommits()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Ui.ClickTile(new Vector3I(4, 0, 2));

    Assert.False(battle.Ui.ClickTile(new Vector3I(3, 0, 1))); // different valid tile: re-target
    Assert.Equal(UiState.TargetingLocked, battle.Ui.State);
    Assert.Equal(new Vector3I(3, 0, 1), battle.Ui.PendingTarget.RequireSome());

    Assert.True(battle.Ui.ClickTile(new Vector3I(3, 0, 1))); // same tile: commit
    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
    var board = battle.Runtime.TryGetTile(new Vector3I(3, 0, 1)).RequireSome();
    Assert.Equal(
      battle.PlayerUnit,
      battle.Runtime.Query(new GetUnitAtTile(board)).RequireSome());
  }

  [TestCase(TestName = "Attack is click-to-fire and enters BattleOver when it ends the battle")]
  public void AttackIsClickToFire()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    AttackActionOption attack = battle.Ui.ActionOptions.AsValueEnumerable().OfType<AttackActionOption>().Single();
    battle.Ui.BeginAction(attack);
    Assert.Equal(UiState.Targeting, battle.Ui.State);

    Assert.True(battle.Ui.ClickTile(new Vector3I(4, 0, 4))); // enemy tile: fires immediately

    Assert.Equal(UiState.BattleOver, battle.Ui.State);
    Assert.True(battle.Runtime.TryGetAlive(battle.EnemyUnit).IsNone); // dead
  }

  [TestCase(TestName = "Escape ladder: locked -> targeting -> selected -> unselected")]
  public void EscapeLadderWalksBack()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Ui.ClickTile(new Vector3I(4, 0, 2));
    Assert.Equal(UiState.TargetingLocked, battle.Ui.State);

    battle.Ui.Cancel();
    Assert.Equal(UiState.Targeting, battle.Ui.State);
    battle.Ui.Cancel();
    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
    battle.Ui.Cancel();
    Assert.Equal(UiState.Unselected, battle.Ui.State);
  }

  [TestCase(TestName = "Instant verbs submit directly and stay in UnitSelected")]
  public void InstantVerbsSubmitDirectly()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    EndTurnActionOption endTurn = battle.Ui.ActionOptions.AsValueEnumerable().OfType<EndTurnActionOption>().Single();

    battle.Ui.BeginAction(endTurn);

    Assert.Equal(UiState.UnitSelected, battle.Ui.State); // selection persisted
    Assert.Equal(battle.EnemyFaction, battle.Runtime.Query(new GetActiveSideQuery()));
  }

  [TestCase(TestName = "PreviewAt updates LastPreview only while targeting")]
  public void PreviewAtUpdatesLastPreviewOnlyWhileTargeting()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsLeft); // not targeting: Left

    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsRight);
    Assert.True(battle.Ui.LastPreview.IsSome);
  }

  [TestCase(TestName = "Busy playback closes the gate and blocks intents")]
  public void BusyPlaybackBlocksIntents()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Ui.ClickTile(new Vector3I(4, 0, 2));

    battle.Busy = true;
    Assert.Equal(InputGate.PlaybackBusy, battle.Ui.Gate);

    Assert.False(battle.Ui.Confirm());
    Assert.Equal(UiState.TargetingLocked, battle.Ui.State);

    battle.Busy = false;
    Assert.Equal(InputGate.Open, battle.Ui.Gate);
    Assert.True(battle.Ui.Confirm());
    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
  }

  [TestCase(TestName = "Enemy turn closes the gate; selection persists and reopens")]
  public void EnemyTurnClosesGateAndSelectionPersists()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.PlayerFaction));

    Assert.Equal(InputGate.NotPlayerTurn, battle.Ui.Gate);
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
    Assert.Equal(battle.PlayerUnit, battle.Ui.SelectedUnit.RequireSome());

    battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.EnemyFaction));

    Assert.Equal(InputGate.Open, battle.Ui.Gate);
    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
  }

  [TestCase(TestName = "Enemy action that kills the selected unit deselects via the signal")]
  public void EnemyKillDeselectsViaSignal()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(battle.PlayerUnit).RequireSome(), 999));

    Assert.Equal(UiState.Unselected, battle.Ui.State);
  }

  [TestCase(TestName = "Switching selection refreshes readouts without a state transition")]
  public void SwitchingSelectionRefreshesReadoutsOnly()
  {
    using var battle = BattleFixture.UiBattle();
    int stateChanges = 0;
    int readoutsChanged = 0;
    battle.Ui.StateChanged += () => stateChanges++;
    battle.Ui.ReadoutsChanged += () => readoutsChanged++;

    Assert.True(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));  // Hero
    Assert.True(battle.Ui.TrySelectAt(new Vector3I(7, 0, 7)));  // Support: switch

    Assert.Equal(UiState.UnitSelected, battle.Ui.State);
    Assert.Equal(1, stateChanges);          // transition fired once (Unselected -> UnitSelected)
    Assert.True(readoutsChanged >= 2);      // both selections mutated readouts
    Assert.Equal(battle.SupportUnit, battle.Ui.SelectedUnit.RequireSome());
    // The cached options now belong to the NEW unit:
    Assert.True(battle.Ui.ActionOptions.AsValueEnumerable().All(o => ReferenceEquals(o.Unit.State, battle.SupportUnit)));
  }

  [TestCase(TestName = "Invalid hover clears the stale preview; gated previews are refused")]
  public void InvalidHoverClearsStalePreview()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());

    Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsRight);
    Assert.True(battle.Ui.LastPreview.IsSome);

    Assert.True(battle.Ui.PreviewAt(new Vector3I(0, 0, 6)).IsLeft);   // non-candidate
    Assert.True(battle.Ui.LastPreview.IsNone);                         // stale cleared

    battle.Busy = true;
    Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsLeft);   // gated, no side effects
    Assert.True(battle.Ui.LastPreview.IsNone);
  }
}
