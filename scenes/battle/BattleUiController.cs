using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;

// Presentation-side UI state machine (pure C#, no Godot beyond Vector3I). Owns selection, the
// verb options cache, and the targeting sub-states. Two transition sources, and only two:
// (1) non-submission intents (select, cancel, begin targeting) run inside the intent method;
// (2) every submission-caused transition lives in OnActionResult, the runtime's ActionCompleted
// signal handler — the FSM reacts identically no matter which actor submitted (player, stub, AI).
// While in Targeting/TargetingLocked the gate is open and nothing is in flight, so any result
// arriving there was caused by the FSM's own submission — no per-action bookkeeping.
public enum UiState { Unselected, UnitSelected, Targeting, TargetingLocked, BattleOver }
public enum InputGate { Open, PlaybackBusy, NotPlayerTurn }

public sealed class BattleUiController : IDisposable
{
  private readonly BattleRuntime _runtime;
  private readonly Faction _playerFaction;
  private readonly Func<bool> _playbackBusy;

  private UiState _state = UiState.Unselected;
  private Option<BattleUnitState> _selected;
  private IReadOnlyList<UnitActionOption> _options = [];
  private Option<IActionTargeting> _targeter;
  private Option<Vector3I> _pendingTarget;
  private Option<ActionPreview> _lastPreview;

  public event Action StateChanged = delegate { };

  // Raised whenever readouts mutate without a state transition (selection switch, options
  // refresh). StateChanged stays transition-only; HUD refresh subscribes to both.
  public event Action ReadoutsChanged = delegate { };

  public BattleUiController(BattleRuntime runtime, Faction playerFaction, Func<bool> playbackBusy)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(playerFaction);
    ArgumentNullException.ThrowIfNull(playbackBusy);
    _runtime = runtime;
    _playerFaction = playerFaction;
    _playbackBusy = playbackBusy;
    _runtime.ActionCompleted += OnActionResult;
  }

  public UiState State => _state;

  // Derived on demand, never stored: playback busy beats everything, then turn/phase facts.
  public InputGate Gate
  {
    get
    {
      if (_playbackBusy())
        return InputGate.PlaybackBusy;
      if (_runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.InProgress)
        return InputGate.NotPlayerTurn;
      if (!ReferenceEquals(_runtime.Query(new GetActiveSideQuery()), _playerFaction))
        return InputGate.NotPlayerTurn;
      return InputGate.Open;
    }
  }

  public Option<BattleUnitState> SelectedUnit => _selected;
  public IReadOnlyList<UnitActionOption> ActionOptions => _options;
  public Option<ActionPreview> LastPreview => _lastPreview;
  public Option<Vector3I> PendingTarget => _pendingTarget;

  public IReadOnlyCollection<Vector3I> CandidateCells =>
    _targeter.Match(handler => handler.Candidates, () => System.Array.Empty<Vector3I>());

  public void Dispose() => _runtime.ActionCompleted -= OnActionResult;

  public bool TrySelectAt(Vector3I tile)
  {
    if (Gate != InputGate.Open || _state == UiState.BattleOver)
      return false;

    return _runtime.TryGetTile(tile).Bind(point => _runtime.Query(new GetUnitAtTile(point))).Match(
      Some: unit =>
      {
        if (!ReferenceEquals(unit.Side, _playerFaction))
          return false;
        ResetTargeting();
        _selected = Some(unit);
        RefreshOptions();
        ReadoutsChanged.Invoke();
        SetState(UiState.UnitSelected);
        return true;
      },
      None: () => false);
  }

  public void Cancel()
  {
    if (Gate != InputGate.Open || _state == UiState.BattleOver)
      return;

    switch (_state)
    {
      case UiState.TargetingLocked:
        _pendingTarget = None;
        _lastPreview = None;
        SetState(UiState.Targeting);
        break;
      case UiState.Targeting:
        ResetTargeting();
        SetState(UiState.UnitSelected);
        break;
      default:
        ResetTargeting();
        _selected = None;
        _options = [];
        ReadoutsChanged.Invoke();
        SetState(UiState.Unselected);
        break;
    }
  }

  public void BeginAction(UnitActionOption option)
  {
    ArgumentNullException.ThrowIfNull(option);
    if (Gate != InputGate.Open || _state != UiState.UnitSelected)
      return;

    switch (option)
    {
      case NeedsTargeting targeted:
        IActionTargeting handler = targeted.Targeting(_runtime);
        _targeter = Some(handler);
        handler.Begin();
        _pendingTarget = None;
        _lastPreview = None;
        SetState(UiState.Targeting);
        break;
      case CanActDirectly instant:
        // OnActionResult (via the signal) refreshes options synchronously.
        _runtime.ExecuteAction(instant.MakeAction());
        break;
      default:
        throw new InvalidOperationException($"Unhandled action option {option.GetType().Name}.");
    }
  }

  public bool ClickTile(Vector3I tile)
  {
    if (Gate != InputGate.Open || _state == UiState.BattleOver)
      return false;

    switch (_state)
    {
      case UiState.Unselected:
      case UiState.UnitSelected:
        return TrySelectAt(tile);
      case UiState.Targeting:
        {
          IActionTargeting handler = RequireHandler();
          if (!handler.CanCommit(tile))
            return false;
          if (handler.Confirm == ConfirmMode.Immediate)
          {
            _runtime.ExecuteAction(handler.Build(tile));
            return true; // OnActionResult resets targeting and lands us in UnitSelected
          }
          _pendingTarget = Some(tile);
          _lastPreview = handler.Preview(tile).Match(
            Right: preview => Some(preview),
            Left: _ => None);
          SetState(UiState.TargetingLocked);
          return true;
        }
      case UiState.TargetingLocked:
        {
          IActionTargeting handler = RequireHandler();
          if (_pendingTarget == Some(tile))
          {
            _runtime.ExecuteAction(handler.Build(tile));
            return true;
          }
          if (handler.CanCommit(tile))
          {
            _pendingTarget = Some(tile);
            _lastPreview = handler.Preview(tile).Match(
              Right: preview => Some(preview),
              Left: _ => None);
          }
          return false;
        }
      default:
        return false;
    }
  }

  public bool Confirm()
  {
    if (Gate != InputGate.Open || _state != UiState.TargetingLocked)
      return false;

    Vector3I target = _pendingTarget.Match(
      t => t,
      () => throw new InvalidOperationException("No pending target in TargetingLocked."));
    _runtime.ExecuteAction(RequireHandler().Build(target));
    return true;
  }

  public Either<BattleQueryFailure, ActionPreview> PreviewAt(Vector3I tile)
  {
    if (Gate != InputGate.Open || _state != UiState.Targeting)
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "UI is not targeting or input is gated."));

    IActionTargeting handler = RequireHandler();
    if (!handler.CanCommit(tile))
    {
      _lastPreview = None;
      return Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "Target is not a candidate."));
    }

    Either<BattleQueryFailure, ActionPreview> result = handler.Preview(tile);
    if (result.IsRight)
      result.IfRight(preview => _lastPreview = Some(preview));
    else
      _lastPreview = None;
    return result;
  }

  // The single submission-caused transition point. Runs synchronously inside
  // runtime.ExecuteAction, after the submission's state mutations are committed.
  private void OnActionResult(BattleActionExecResult result)
  {
    if (_state == UiState.BattleOver)
      return;

    if (_runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.InProgress)
    {
      ResetTargeting();
      _selected = None;
      _options = [];
      ReadoutsChanged.Invoke();
      SetState(UiState.BattleOver);
      return;
    }

    _selected = _selected.Bind(_runtime.TryGetAlive).Map(alive => alive.State);
    if (_selected.IsNone)
    {
      ResetTargeting();
      _options = [];
      ReadoutsChanged.Invoke();
      SetState(UiState.Unselected);
      return;
    }

    if (_state is UiState.Targeting or UiState.TargetingLocked)
    {
      ResetTargeting();
      SetState(UiState.UnitSelected);
    }

    RefreshOptions();
    ReadoutsChanged.Invoke();
  }

  private void RefreshOptions()
  {
    // Mint the aliveness proof at the query door; a dead selected unit yields empty options.
    _options = _selected.Bind(_runtime.TryGetAlive).Match(
      Some: proof => BuildOptions(proof, _runtime.Query(new GetAvailableActionsForUnit(proof))),
      None: []);
  }

  private static IReadOnlyList<UnitActionOption> BuildOptions(
    AliveUnit unit, IReadOnlyList<UnitAction> actions) =>
    actions.AsValueEnumerable().Select(action => MakeOption(unit, action)).ToList();

  private static UnitActionOption MakeOption(AliveUnit unit, UnitAction action) => action.Action switch
  {
    MoveActionDefinition => new MoveActionOption(unit, action.IsAvailable),
    AttackActionDefinition => new AttackActionOption(unit, RequireWeapon(unit.State), action.IsAvailable),
    ReloadActionDefinition => new ReloadActionOption(unit, action.IsAvailable),
    PassActionDefinition => new PassActionOption(unit, action.IsAvailable),
    EndTurnActionDefinition => new EndTurnActionOption(unit, action.IsAvailable),
    _ => throw new InvalidOperationException($"No presentation option for {action.Action.GetType().Name}."),
  };

  private static Weapon RequireWeapon(BattleUnitState unit) => unit.EquippedWeapon.Match(
    Some: weapon => weapon,
    None: () => throw new InvalidOperationException("Attack option requires an equipped weapon."));

  private void ResetTargeting()
  {
    _targeter = None;
    _pendingTarget = None;
    _lastPreview = None;
  }

  private IActionTargeting RequireHandler() =>
    _targeter.Match(handler => handler, () => throw new InvalidOperationException("No active targeting handler."));

  private void SetState(UiState next)
  {
    if (_state == next)
      return;
    _state = next;
    StateChanged.Invoke();
  }
}
