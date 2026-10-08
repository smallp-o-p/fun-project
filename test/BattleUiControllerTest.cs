using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using System;
using System.Collections.Generic;

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
    battle.Read(() => Assert.Equal(UiState.Unselected, battle.Ui.State));
    battle.Read(() => Assert.Equal(InputGate.Open, battle.Ui.Gate));

    Assert.True(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));

    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(battle.PlayerUnit, battle.Ui.SelectedUnit.RequireSome()));
    battle.Read(() => Assert.True(battle.Ui.ActionOptions.Count > 0));
  }

  [TestCase(TestName = "Action options retain identity and reflect live availability across selection changes")]
  public void ActionOptionsRetainIdentityAndLiveAvailability()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    IReadOnlyList<UnitActionOption> options = battle.Ui.ActionOptions;
    MoveActionOption move = options.AsValueEnumerable().OfType<MoveActionOption>().Single();

    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)], actionPointCost: 4);

    battle.Read(() => Assert.True(ReferenceEquals(options, battle.Ui.ActionOptions)));
    battle.Read(() => Assert.False(move.IsAvailable));

    battle.Ui.TrySelectAt(new Vector3I(7, 0, 7));
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 2));

    battle.Read(() => Assert.True(ReferenceEquals(options, battle.Ui.ActionOptions)));
  }

  [TestCase(TestName = "Disposal clears retained UI state and detaches runtime handling")]
  public void DisposalClearsStateAndDetachesRuntimeHandling()
  {
    using var battle = BattleFixture.UiBattle();
    BattleUiController ui = battle.Ui;
    battle.Read(() => Assert.True(ui.TrySelectAt(new Vector3I(4, 0, 1))));
    ui.BeginAction(ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Read(() => Assert.True(ui.ClickTile(new Vector3I(4, 0, 2))));
    battle.Read(() => Assert.True(ui.ActionOptions.Count > 0));
    battle.Read(() => Assert.True(ui.CandidateCells.Count > 0));
    battle.Read(() => Assert.True(ui.PendingTarget.IsSome));
    battle.Read(() => Assert.True(ui.LastPreview.IsSome));
    int stateChanges = 0;
    int readoutsChanged = 0;
    ui.StateChanged += () => stateChanges++;
    ui.ReadoutsChanged += () => readoutsChanged++;

    ui.Dispose();

    battle.Read(() => Assert.True(ui.SelectedUnit.IsNone));
    battle.Read(() => Assert.Equal(0, ui.ActionOptions.Count));
    battle.Read(() => Assert.Equal(0, ui.CandidateCells.Count));
    battle.Read(() => Assert.True(ui.PendingTarget.IsNone));
    battle.Read(() => Assert.True(ui.LastPreview.IsNone));

    battle.Move(battle.SupportUnit, [new Vector3I(6, 0, 7)]);

    battle.Read(() => Assert.Equal(0, stateChanges));
    battle.Read(() => Assert.Equal(0, readoutsChanged));
    battle.Read(() => Assert.True(ui.SelectedUnit.IsNone));
    battle.Read(() => Assert.Equal(0, ui.ActionOptions.Count));
    battle.Read(() => Assert.Equal(0, ui.CandidateCells.Count));
    battle.Read(() => Assert.True(ui.PendingTarget.IsNone));
    battle.Read(() => Assert.True(ui.LastPreview.IsNone));
  }

  [TestCase(TestName = "Clicking enemy or empty tiles does not select")]
  public void ClickingEnemyOrEmptyTilesDoesNotSelect()
  {
    using var battle = BattleFixture.UiBattle();
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 4))); // enemy tile
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(0, 0, 0))); // empty tile
    battle.Read(() => Assert.Equal(UiState.Unselected, battle.Ui.State));
  }

  [TestCase(TestName = "Cancel deselects; StateChanged fires only on transitions")]
  public void CancelDeselectsAndStateChangedFiresOnlyOnTransitions()
  {
    using var battle = BattleFixture.UiBattle();
    int changes = 0;
    battle.Ui.StateChanged += () => changes++;

    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)); // re-select same unit: no transition
    battle.Read(() => Assert.Equal(1, changes));

    battle.Ui.Cancel();
    battle.Read(() => Assert.Equal(UiState.Unselected, battle.Ui.State));
    battle.Read(() => Assert.True(battle.Ui.SelectedUnit.IsNone));
    battle.Read(() => Assert.Equal(2, changes));
  }

  [TestCase(UiState.UnitSelected, 20, DamageKind.Stun, TestName = "A foreign stun of the selected unit deselects")]
  [TestCase(UiState.Targeting, 20, DamageKind.Stun, TestName = "A foreign stun during targeting deselects and clears actions")]
  [TestCase(UiState.TargetingLocked, 20, DamageKind.Stun, TestName = "A foreign stun during locked targeting deselects and clears actions")]
  [TestCase(UiState.UnitSelected, 999, DamageKind.Health, TestName = "A foreign kill of the selected unit deselects")]
  public void ForeignIncapacitationDeselectsAndClearsActions(UiState initialState, int amount, DamageKind kind)
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    if (initialState is UiState.Targeting or UiState.TargetingLocked)
    {
      battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
      battle.Ui.PreviewAt(new Vector3I(4, 0, 2));
      if (initialState == UiState.TargetingLocked)
        battle.Ui.ClickTile(new Vector3I(4, 0, 2));
    }
    battle.Read(() => Assert.Equal(initialState, battle.Ui.State));

    // Genuine runtime submission path; the surviving Support keeps this short of BattleOver.
    battle.ApplyDamage(battle.PlayerUnit, amount, kind);

    battle.Read(() => Assert.Equal(UiState.Unselected, battle.Ui.State));
    battle.Read(() => Assert.True(battle.Ui.SelectedUnit.IsNone));
    battle.Read(() => Assert.Equal(0, battle.Ui.ActionOptions.Count));
    battle.Read(() => Assert.Equal(0, battle.Ui.CandidateCells.Count));
    battle.Read(() => Assert.True(battle.Ui.PendingTarget.IsNone));
    battle.Read(() => Assert.True(battle.Ui.LastPreview.IsNone));
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
  }

  [TestCase(TestName = "A submission that ends the battle enters terminal BattleOver")]
  public void BattleEndEntersTerminalBattleOver()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(battle.EnemyUnit).RequireSome(), 999)); // the last opposing unit falls: battle over

    battle.Read(() => Assert.True(battle.Runtime.Query(new GetBattleOutcomeQuery()).IsSome));
    battle.Read(() => Assert.Equal(UiState.BattleOver, battle.Ui.State));
    battle.Read(() => Assert.True(battle.Ui.SelectedUnit.IsNone));

    // Terminal: further intents are no-ops, gate stays meaningless-but-closed.
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    battle.Read(() => Assert.Equal(UiState.BattleOver, battle.Ui.State));
  }

  [TestCase(TestName = "A factory result that already has an outcome starts BattleOver with closed input")]
  public void StartupTerminalFactoryResultStartsBattleOver()
  {
    // The factory finishes bootstrap before returning, so this terminal first-turn objective
    // is decided before the controller is even constructed — no action result can deliver it.
    using var battle = BattleFixture.Duel(playerControlled: true, configure: setup => setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Objectives = [new SurviveUntilTurnObjectiveData
          {
            TargetTurn = 1,
            OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
          }],
        },
        setup.Sides[1],
      ],
    });

    battle.Read(() => Assert.True(battle.Query(new GetBattleOutcomeQuery()).IsSome));
    battle.Read(() => Assert.Equal(UiState.BattleOver, battle.Ui.State));
    battle.Read(() => Assert.Equal(InputGate.NotPlayerTurn, battle.Ui.Gate));
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    battle.Read(() => Assert.Equal(UiState.BattleOver, battle.Ui.State));
  }

  [TestCase(TestName = "BeginAction enters targeting with candidates; invalid clicks no-op")]
  public void BeginActionEntersTargetingWithCandidates()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    MoveActionOption move = battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single();

    battle.Ui.BeginAction(move);

    battle.Read(() => Assert.Equal(UiState.Targeting, battle.Ui.State));
    battle.Read(() => Assert.True(battle.Ui.CandidateCells.Count > 0));

    // A tile outside the candidate set is a no-op, still targeting.
    Assert.False(battle.Ui.ClickTile(new Vector3I(0, 0, 6)));
    battle.Read(() => Assert.Equal(UiState.Targeting, battle.Ui.State));
  }

  [TestCase(false, TestName = "A disabled retained option starts neither targeting nor a submission")]
  [TestCase(true, TestName = "An option retained from another selection cannot act")]
  public void RetainedPassOptionCannotAct(bool foreign)
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    PassActionOption pass = battle.Ui.ActionOptions.AsValueEnumerable().OfType<PassActionOption>().Single();
    if (foreign)
      battle.Ui.TrySelectAt(new Vector3I(7, 0, 7)); // retained from the player Hero
    else
      battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)], actionPointCost: 4); // pass costs all remaining AP
    int submissions = 0;
    battle.Runtime.ActionCompleted += _ => submissions++;
    battle.ClearEvents();

    if (!foreign)
      battle.Read(() => Assert.False(pass.IsAvailable));
    battle.Ui.BeginAction(pass);

    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(0, battle.Ui.CandidateCells.Count));
    battle.Read(() => Assert.Equal(0, submissions));
    battle.Read(() => Assert.Equal(0, battle.Events.Count));
    if (foreign)
      battle.Read(() => Assert.Equal(battle.SupportUnit, battle.Ui.SelectedUnit.RequireSome()));
  }

  [TestCase(TestName = "A boxed-in available move can enter empty targeting and cancel")]
  public void BoxedInAvailableMoveCanCancelEmptyTargeting()
  {
    using var battle = BattleFixture.Duel(map: TestData.MakeMapWithWalls(new Vector3I(8, 1, 8),
      new Vector3I(4, 0, 0), new Vector3I(4, 0, 2), new Vector3I(3, 0, 1), new Vector3I(5, 0, 1)),
      playerControlled: true);
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    MoveActionOption move = battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single();

    battle.Read(() => Assert.True(move.IsAvailable));
    battle.Ui.BeginAction(move);

    battle.Read(() => Assert.Equal(UiState.Targeting, battle.Ui.State));
    battle.Read(() => Assert.Equal(0, battle.Ui.CandidateCells.Count));
    battle.Ui.Cancel();
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
  }

  [TestCase(TestName = "An available out-of-range attack can enter empty targeting and cancel")]
  public void OutOfRangeAvailableAttackCanCancelEmptyTargeting()
  {
    using var battle = BattleFixture.Duel(
      player: new("Hero", Position: new Vector3I(0, 0, 0), Weapon: TestData.MakeWeapon("Knife", range: 1)),
      enemy: new("Goon", Position: new Vector3I(7, 0, 7)),
      playerControlled: true);
    battle.Ui.TrySelectAt(new Vector3I(0, 0, 0));
    AttackActionOption attack = battle.Ui.ActionOptions.AsValueEnumerable().OfType<AttackActionOption>().Single();

    battle.Read(() => Assert.True(attack.IsAvailable));
    battle.Ui.BeginAction(attack);

    battle.Read(() => Assert.Equal(UiState.Targeting, battle.Ui.State));
    battle.Read(() => Assert.Equal(0, battle.Ui.CandidateCells.Count));
    battle.Ui.Cancel();
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
  }

  [TestCase(TestName = "Move locks then confirms; commit exits targeting via the signal")]
  public void MoveLocksThenConfirmsAndCommits()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());

    Assert.True(battle.Ui.ClickTile(new Vector3I(4, 0, 2))); // Confirm mode: locks
    battle.Read(() => Assert.Equal(UiState.TargetingLocked, battle.Ui.State));
    battle.Read(() => Assert.Equal(new Vector3I(4, 0, 2), battle.Ui.PendingTarget.RequireSome()));

    Assert.True(battle.Ui.Confirm()); // submits; OnActionResult exits targeting synchronously

    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(0, battle.Ui.CandidateCells.Count));
    var board = battle.Runtime.TryGetTile(new Vector3I(4, 0, 2)).RequireSome();
    battle.Read(() => Assert.Equal(
      battle.PlayerUnit,
      battle.Runtime.Query(new GetUnitAtTile(board)).RequireSome()));
  }

  [TestCase(TestName = "Re-clicking a different tile re-targets; same tile commits")]
  public void LockedRetargetThenSameTileCommits()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Ui.ClickTile(new Vector3I(4, 0, 2));

    Assert.False(battle.Ui.ClickTile(new Vector3I(3, 0, 1))); // different valid tile: re-target
    battle.Read(() => Assert.Equal(UiState.TargetingLocked, battle.Ui.State));
    battle.Read(() => Assert.Equal(new Vector3I(3, 0, 1), battle.Ui.PendingTarget.RequireSome()));

    Assert.True(battle.Ui.ClickTile(new Vector3I(3, 0, 1))); // same tile: commit
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    var board = battle.Runtime.TryGetTile(new Vector3I(3, 0, 1)).RequireSome();
    battle.Read(() => Assert.Equal(
      battle.PlayerUnit,
      battle.Runtime.Query(new GetUnitAtTile(board)).RequireSome()));
  }

  [TestCase(TestName = "Attack is click-to-fire and enters BattleOver when it ends the battle")]
  public void AttackIsClickToFire()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    AttackActionOption attack = battle.Ui.ActionOptions.AsValueEnumerable().OfType<AttackActionOption>().Single();
    battle.Ui.BeginAction(attack);
    battle.Read(() => Assert.Equal(UiState.Targeting, battle.Ui.State));

    Assert.True(battle.Ui.ClickTile(new Vector3I(4, 0, 4))); // enemy tile: fires immediately

    battle.Read(() => Assert.Equal(UiState.BattleOver, battle.Ui.State));
    battle.Read(() => Assert.True(battle.Runtime.TryGetAlive(battle.EnemyUnit).IsNone)); // dead
  }

  [TestCase(TestName = "Escape ladder: locked -> targeting -> selected -> unselected")]
  public void EscapeLadderWalksBack()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Ui.ClickTile(new Vector3I(4, 0, 2));
    battle.Read(() => Assert.Equal(UiState.TargetingLocked, battle.Ui.State));

    battle.Ui.Cancel();
    battle.Read(() => Assert.Equal(UiState.Targeting, battle.Ui.State));
    battle.Ui.Cancel();
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Ui.Cancel();
    battle.Read(() => Assert.Equal(UiState.Unselected, battle.Ui.State));
  }

  [TestCase(TestName = "Retained direct options refuse to mint actions after their unit dies")]
  public void RetainedDirectOptionsRequireFreshAliveProof()
  {
    using var battle = BattleFixture.Duel(
      player: new("Hero", Weapon: TestData.MakeAmmoWeapon("Rifle")),
      playerControlled: true);
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    PassActionOption pass = battle.Ui.ActionOptions.AsValueEnumerable().OfType<PassActionOption>().Single();
    EndTurnActionOption endTurn = battle.Ui.ActionOptions.AsValueEnumerable().OfType<EndTurnActionOption>().Single();
    ReloadActionOption reload = battle.Ui.ActionOptions.AsValueEnumerable().OfType<ReloadActionOption>().Single();

    battle.ApplyDamage(battle.PlayerUnit, 999);

    Assert.Throws<InvalidOperationException>(() => pass.MakeAction(battle.Runtime));
    Assert.Throws<InvalidOperationException>(() => endTurn.MakeAction(battle.Runtime));
    Assert.Throws<InvalidOperationException>(() => reload.MakeAction(battle.Runtime));
  }

  [TestCase(TestName = "Instant verbs submit directly and stay in UnitSelected")]
  public void InstantVerbsSubmitDirectly()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    EndTurnActionOption endTurn = battle.Ui.ActionOptions.AsValueEnumerable().OfType<EndTurnActionOption>().Single();

    battle.Ui.BeginAction(endTurn);

    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State)); // selection persisted
    battle.Read(() => Assert.Equal(battle.EnemyFaction, battle.Runtime.Query(new GetActiveSideQuery())));
  }

  [TestCase(TestName = "PreviewAt updates LastPreview only while targeting")]
  public void PreviewAtUpdatesLastPreviewOnlyWhileTargeting()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Read(() => Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsLeft)); // not targeting: Left

    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Read(() => Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsRight));
    battle.Read(() => Assert.True(battle.Ui.LastPreview.IsSome));
  }

  [TestCase(TestName = "Busy playback closes the gate and blocks intents")]
  public void BusyPlaybackBlocksIntents()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());
    battle.Ui.ClickTile(new Vector3I(4, 0, 2));

    battle.Busy = true;
    battle.Read(() => Assert.Equal(InputGate.PlaybackBusy, battle.Ui.Gate));

    Assert.False(battle.Ui.Confirm());
    battle.Read(() => Assert.Equal(UiState.TargetingLocked, battle.Ui.State));

    battle.Busy = false;
    battle.Read(() => Assert.Equal(InputGate.Open, battle.Ui.Gate));
    Assert.True(battle.Ui.Confirm());
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
  }

  [TestCase(TestName = "Enemy turn closes the gate; selection persists and reopens")]
  public void EnemyTurnClosesGateAndSelectionPersists()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));

    battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.PlayerFaction));

    battle.Read(() => Assert.Equal(InputGate.NotPlayerTurn, battle.Ui.Gate));
    Assert.False(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(battle.PlayerUnit, battle.Ui.SelectedUnit.RequireSome()));

    battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.EnemyFaction));

    battle.Read(() => Assert.Equal(InputGate.Open, battle.Ui.Gate));
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
  }

  [TestCase]
  public void ExhaustedActionPointsKeepSelectionAndAllowReselection()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)], actionPointCost: 4);

    battle.Read(() => Assert.Equal(0, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(battle.PlayerUnit, battle.Ui.SelectedUnit.RequireSome()));
    battle.Ui.Cancel();
    Assert.True(battle.Ui.TrySelectAt(new Vector3I(4, 0, 2)));
  }

  [TestCase]
  public void TemporaryImmobilizationKeepsSelectionAndAllowsReselection()
  {
    using var battle = BattleFixture.Duel(
      player: new("Hero"),
      enemy: new("Goon", Health: 10,
        Weapon: TestData.MakeStatusWeapon(TestData.MakeStun(duration: 2), damage: 1)),
      hitChanceCalculator: new AlwaysHitCalculator(), playerControlled: true);
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.Attack(battle.EnemyUnit, battle.PlayerUnit);

    battle.Read(() => Assert.True(battle.PlayerUnit.IsImmobilized));
    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(battle.PlayerUnit, battle.Ui.SelectedUnit.RequireSome()));
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.Ui.Cancel();
    Assert.True(battle.Ui.TrySelectAt(new Vector3I(4, 0, 1)));
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

    battle.Read(() => Assert.Equal(UiState.UnitSelected, battle.Ui.State));
    battle.Read(() => Assert.Equal(1, stateChanges));          // transition fired once (Unselected -> UnitSelected)
    battle.Read(() => Assert.True(readoutsChanged >= 2));      // both selections mutated readouts
    battle.Read(() => Assert.Equal(battle.SupportUnit, battle.Ui.SelectedUnit.RequireSome()));
    // The cached options now belong to the NEW unit:
    battle.Read(() => Assert.True(battle.Ui.ActionOptions.AsValueEnumerable().All(o => ReferenceEquals(o.Unit, battle.SupportUnit))));
  }

  [TestCase(TestName = "Invalid hover clears the stale preview; gated previews are refused")]
  public void InvalidHoverClearsStalePreview()
  {
    using var battle = BattleFixture.UiBattle();
    battle.Ui.TrySelectAt(new Vector3I(4, 0, 1));
    battle.Ui.BeginAction(battle.Ui.ActionOptions.AsValueEnumerable().OfType<MoveActionOption>().Single());

    battle.Read(() => Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsRight));
    battle.Read(() => Assert.True(battle.Ui.LastPreview.IsSome));

    battle.Read(() => Assert.True(battle.Ui.PreviewAt(new Vector3I(0, 0, 6)).IsLeft));   // non-candidate
    battle.Read(() => Assert.True(battle.Ui.LastPreview.IsNone));                         // stale cleared

    battle.Busy = true;
    battle.Read(() => Assert.True(battle.Ui.PreviewAt(new Vector3I(4, 0, 2)).IsLeft));   // gated, no side effects
    battle.Read(() => Assert.True(battle.Ui.LastPreview.IsNone));
  }
}
