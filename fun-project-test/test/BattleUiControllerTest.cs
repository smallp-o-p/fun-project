using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleUiControllerTest
{
  private sealed class UiFixture : IDisposable
  {
    public BattleRuntime Runtime { get; }
    public Faction PlayerFaction { get; }
    public Faction EnemyFaction { get; }
    public BattleTestUnit PlayerUnit { get; }
    public BattleTestUnit SupportUnit { get; }
    public BattleTestUnit EnemyUnit { get; }
    public BattleUiController Ui { get; }
    public bool Busy; // fake playback-busy flag, flipped by tests

    public UiFixture()
    {
      PlayerFaction = BattleTestFactory.MakeFaction("Player");
      EnemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(
        new Vector3I(8, 1, 8),
        [PlayerFaction, EnemyFaction],
        new AlwaysHitCalculator(),
        playerFaction: Some(PlayerFaction));
      Runtime = new BattleRuntime(session);

      var heroPoint = Runtime.TryGetTile(new Vector3I(4, 0, 1)).RequireSome();
      Runtime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Hero", PlayerFaction),
        heroPoint,
        BattleTestFactory.MakeWeapon("Rifle", damage: 10)));
      PlayerUnit = new BattleTestUnit(Runtime.Query(new GetUnitAtTile(heroPoint)).RequireSome());

      EnemyUnit = BattleActionTestHelper.SpawnUnit(
        Runtime,
        BattleTestFactory.MakeCombatant("Goon", EnemyFaction, health: 10),
        new Vector3I(4, 0, 4));

      // Keep the player faction alive when the selected Hero is killed, while preserving
      // the fixture's specified Hero/Goon positions and the empty-tile assertion.
      var supportPoint = Runtime.TryGetTile(new Vector3I(7, 0, 7)).RequireSome();
      Runtime.ExecuteAction(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Support", PlayerFaction), supportPoint));
      SupportUnit = new BattleTestUnit(Runtime.Query(new GetUnitAtTile(supportPoint)).RequireSome());

      BattleActionTestHelper.EnsureEveryFactionHasObjective(session);
      BattleActionTestHelper.StartBattle(Runtime);

      Ui = new BattleUiController(Runtime, PlayerFaction, () => Busy);
    }

    public void Dispose()
    {
      Ui.Dispose();
      Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Selecting a friendly unit selects it and loads its verbs")]
  public void SelectingFriendlyUnitLoadsVerbs()
  {
    using var fixture = new UiFixture();
    Assert.Equal(UiState.Unselected, fixture.Ui.State);
    Assert.Equal(InputGate.Open, fixture.Ui.Gate);

    Assert.True(fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1)));

    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
    Assert.Equal(fixture.PlayerUnit.State, fixture.Ui.SelectedUnit.RequireSome());
    Assert.True(fixture.Ui.ActionOptions.Count > 0);
  }

  [TestCase(TestName = "Clicking enemy or empty tiles does not select")]
  public void ClickingEnemyOrEmptyTilesDoesNotSelect()
  {
    using var fixture = new UiFixture();
    Assert.False(fixture.Ui.TrySelectAt(new Vector3I(4, 0, 4))); // enemy tile
    Assert.False(fixture.Ui.TrySelectAt(new Vector3I(0, 0, 0))); // empty tile
    Assert.Equal(UiState.Unselected, fixture.Ui.State);
  }

  [TestCase(TestName = "Cancel deselects; StateChanged fires only on transitions")]
  public void CancelDeselectsAndStateChangedFiresOnlyOnTransitions()
  {
    using var fixture = new UiFixture();
    int changes = 0;
    fixture.Ui.StateChanged += () => changes++;

    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1)); // re-select same unit: no transition
    Assert.Equal(1, changes);

    fixture.Ui.Cancel();
    Assert.Equal(UiState.Unselected, fixture.Ui.State);
    Assert.True(fixture.Ui.SelectedUnit.IsNone);
    Assert.Equal(2, changes);
  }

  [TestCase(TestName = "A foreign submission that kills the selected unit deselects")]
  public void ForeignKillDeselects()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    fixture.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      fixture.Runtime.TryGetAlive(fixture.PlayerUnit.State).RequireSome(), 999));

    Assert.Equal(UiState.Unselected, fixture.Ui.State);
    Assert.True(fixture.Ui.SelectedUnit.IsNone);
    Assert.Equal(0, fixture.Ui.ActionOptions.Count);
  }

  [TestCase(TestName = "A submission that ends the battle enters terminal BattleOver")]
  public void BattleEndEntersTerminalBattleOver()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    fixture.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      fixture.Runtime.TryGetAlive(fixture.EnemyUnit.State).RequireSome(), 999));
    fixture.Runtime.ExecuteAction(BattleAction.EndFactionTurn(fixture.PlayerFaction));

    Assert.Equal(BattlePhase.Ended, fixture.Runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(UiState.BattleOver, fixture.Ui.State);
    Assert.True(fixture.Ui.SelectedUnit.IsNone);

    // Terminal: further intents are no-ops, gate stays meaningless-but-closed.
    Assert.False(fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    Assert.Equal(UiState.BattleOver, fixture.Ui.State);
  }

  [TestCase(TestName = "BeginAction enters targeting with candidates; invalid clicks no-op")]
  public void BeginActionEntersTargetingWithCandidates()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    MoveActionOption move = fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single();

    fixture.Ui.BeginAction(move);

    Assert.Equal(UiState.Targeting, fixture.Ui.State);
    Assert.True(fixture.Ui.CandidateCells.Count > 0);

    // A tile outside the candidate set is a no-op, still targeting.
    Assert.False(fixture.Ui.ClickTile(new Vector3I(0, 0, 6)));
    Assert.Equal(UiState.Targeting, fixture.Ui.State);
  }

  [TestCase(TestName = "Move locks then confirms; commit exits targeting via the signal")]
  public void MoveLocksThenConfirmsAndCommits()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    fixture.Ui.BeginAction(fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());

    Assert.True(fixture.Ui.ClickTile(new Vector3I(4, 0, 2))); // Confirm mode: locks
    Assert.Equal(UiState.TargetingLocked, fixture.Ui.State);
    Assert.Equal(new Vector3I(4, 0, 2), fixture.Ui.PendingTarget.RequireSome());

    Assert.True(fixture.Ui.Confirm()); // submits; OnActionResult exits targeting synchronously

    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
    Assert.Equal(0, fixture.Ui.CandidateCells.Count);
    var board = fixture.Runtime.TryGetTile(new Vector3I(4, 0, 2)).RequireSome();
    Assert.Equal(
      fixture.PlayerUnit.State,
      fixture.Runtime.Query(new GetUnitAtTile(board)).RequireSome());
  }

  [TestCase(TestName = "Re-clicking a different tile re-targets; same tile commits")]
  public void LockedRetargetThenSameTileCommits()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    fixture.Ui.BeginAction(fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    fixture.Ui.ClickTile(new Vector3I(4, 0, 2));

    Assert.False(fixture.Ui.ClickTile(new Vector3I(3, 0, 1))); // different valid tile: re-target
    Assert.Equal(UiState.TargetingLocked, fixture.Ui.State);
    Assert.Equal(new Vector3I(3, 0, 1), fixture.Ui.PendingTarget.RequireSome());

    Assert.True(fixture.Ui.ClickTile(new Vector3I(3, 0, 1))); // same tile: commit
    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
    var board = fixture.Runtime.TryGetTile(new Vector3I(3, 0, 1)).RequireSome();
    Assert.Equal(
      fixture.PlayerUnit.State,
      fixture.Runtime.Query(new GetUnitAtTile(board)).RequireSome());
  }

  [TestCase(TestName = "Attack is click-to-fire and enters BattleOver when it ends the battle")]
  public void AttackIsClickToFire()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    AttackActionOption attack = fixture.Ui.ActionOptions.AsValueEnumerable().OfType<AttackActionOption>().Single();
    fixture.Ui.BeginAction(attack);
    Assert.Equal(UiState.Targeting, fixture.Ui.State);

    Assert.True(fixture.Ui.ClickTile(new Vector3I(4, 0, 4))); // enemy tile: fires immediately

    Assert.Equal(UiState.BattleOver, fixture.Ui.State);
    Assert.True(fixture.Runtime.TryGetAlive(fixture.EnemyUnit.State).IsNone); // dead
  }

  [TestCase(TestName = "Escape ladder: locked -> targeting -> selected -> unselected")]
  public void EscapeLadderWalksBack()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    fixture.Ui.BeginAction(fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    fixture.Ui.ClickTile(new Vector3I(4, 0, 2));
    Assert.Equal(UiState.TargetingLocked, fixture.Ui.State);

    fixture.Ui.Cancel();
    Assert.Equal(UiState.Targeting, fixture.Ui.State);
    fixture.Ui.Cancel();
    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
    fixture.Ui.Cancel();
    Assert.Equal(UiState.Unselected, fixture.Ui.State);
  }

  [TestCase(TestName = "Instant verbs submit directly and stay in UnitSelected")]
  public void InstantVerbsSubmitDirectly()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    EndTurnActionOption endTurn = fixture.Ui.ActionOptions.AsValueEnumerable().OfType<EndTurnActionOption>().Single();

    fixture.Ui.BeginAction(endTurn);

    Assert.Equal(UiState.UnitSelected, fixture.Ui.State); // selection persisted
    Assert.Equal(fixture.EnemyFaction, fixture.Runtime.Query(new GetActiveSideQuery()));
  }

  [TestCase(TestName = "PreviewAt updates LastPreview only while targeting")]
  public void PreviewAtUpdatesLastPreviewOnlyWhileTargeting()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    Assert.True(fixture.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsLeft); // not targeting: Left

    fixture.Ui.BeginAction(fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    Assert.True(fixture.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsRight);
    Assert.True(fixture.Ui.LastPreview.IsSome);
  }

  [TestCase(TestName = "Busy playback closes the gate and blocks intents")]
  public void BusyPlaybackBlocksIntents()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    fixture.Ui.BeginAction(fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    fixture.Ui.ClickTile(new Vector3I(4, 0, 2));

    fixture.Busy = true;
    Assert.Equal(InputGate.PlaybackBusy, fixture.Ui.Gate);

    Assert.False(fixture.Ui.Confirm());
    Assert.Equal(UiState.TargetingLocked, fixture.Ui.State);

    fixture.Busy = false;
    Assert.Equal(InputGate.Open, fixture.Ui.Gate);
    Assert.True(fixture.Ui.Confirm());
    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
  }

  [TestCase(TestName = "Enemy turn closes the gate; selection persists and reopens")]
  public void EnemyTurnClosesGateAndSelectionPersists()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    fixture.Runtime.ExecuteAction(BattleAction.EndFactionTurn(fixture.PlayerFaction));

    Assert.Equal(InputGate.NotPlayerTurn, fixture.Ui.Gate);
    Assert.False(fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
    Assert.Equal(fixture.PlayerUnit.State, fixture.Ui.SelectedUnit.RequireSome());

    fixture.Runtime.ExecuteAction(BattleAction.EndFactionTurn(fixture.EnemyFaction));

    Assert.Equal(InputGate.Open, fixture.Ui.Gate);
    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
  }

  [TestCase(TestName = "Enemy action that kills the selected unit deselects via the signal")]
  public void EnemyKillDeselectsViaSignal()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    fixture.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      fixture.Runtime.TryGetAlive(fixture.PlayerUnit.State).RequireSome(), 999));

    Assert.Equal(UiState.Unselected, fixture.Ui.State);
  }

  [TestCase(TestName = "Switching selection refreshes readouts without a state transition")]
  public void SwitchingSelectionRefreshesReadoutsOnly()
  {
    using var fixture = new UiFixture();
    int stateChanges = 0;
    int readoutsChanged = 0;
    fixture.Ui.StateChanged += () => stateChanges++;
    fixture.Ui.ReadoutsChanged += () => readoutsChanged++;

    Assert.True(fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1)));  // Hero
    Assert.True(fixture.Ui.TrySelectAt(new Vector3I(7, 0, 7)));  // Support: switch

    Assert.Equal(UiState.UnitSelected, fixture.Ui.State);
    Assert.Equal(1, stateChanges);          // transition fired once (Unselected -> UnitSelected)
    Assert.True(readoutsChanged >= 2);      // both selections mutated readouts
    Assert.Equal(fixture.SupportUnit.State, fixture.Ui.SelectedUnit.RequireSome());
    // The cached options now belong to the NEW unit:
    Assert.True(fixture.Ui.ActionOptions.AsValueEnumerable().All(o => ReferenceEquals(o.Unit.State, fixture.SupportUnit.State)));
  }

  [TestCase(TestName = "Invalid hover clears the stale preview; gated previews are refused")]
  public void InvalidHoverClearsStalePreview()
  {
    using var fixture = new UiFixture();
    fixture.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    fixture.Ui.BeginAction(fixture.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());

    Assert.True(fixture.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsRight);
    Assert.True(fixture.Ui.LastPreview.IsSome);

    Assert.True(fixture.Ui.PreviewAt(new Vector3I(0, 0, 6)).IsLeft);   // non-candidate
    Assert.True(fixture.Ui.LastPreview.IsNone);                         // stale cleared

    fixture.Busy = true;
    Assert.True(fixture.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsLeft);   // gated, no side effects
    Assert.True(fixture.Ui.LastPreview.IsNone);
  }
}
