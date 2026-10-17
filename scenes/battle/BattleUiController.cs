using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Scenes;

// Presentation-side UI state machine (pure C#, no Godot beyond Vector3I). Owns selection and
// targeting sub-states. The runtime cache owns per-unit option identity; the UI memoizes presentation
// adapters against the runtime's returned lists so the two can never disagree. Two transition sources,
// and only two:
// (1) non-submission intents (select, cancel, begin targeting) run inside the intent method;
// (2) every submission-caused transition lives in OnActionResult, the runtime's ActionCompleted
// signal handler — the FSM reacts identically no matter which actor submitted (player, stub, AI).
// Results can also arrive during targeting from foreign submissions. Selection and targeting
// are reconciled against the committed unit state without per-action bookkeeping.
public enum UiState { Unselected, UnitSelected, Targeting, TargetingLocked, BattleOver }
public enum InputGate { Open, PlaybackBusy, NotPlayerTurn }

public sealed class BattleUiController : IDisposable
{
  private readonly BattleRuntime _runtime;
  private readonly Faction _playerFaction;
  private readonly Func<bool> _playbackBusy;

  private UiState _state;
  private Option<BattleUnitState> _selected;
  private readonly Dictionary<IReadOnlyList<UnitAction>, IReadOnlyList<UnitActionOption>> _optionsByList = [];
  private IReadOnlyList<UnitActionOption> _options = [];
  private Option<IActionTargeting> _targeter;
  private Option<Vector3I> _pendingTarget;
  private Option<ActionPreview> _lastPreview;
  private bool _disposed;

  // Observers see one complete selection/targeting update. Hover previews are read directly.
  public event Action Changed = delegate { };

  public BattleUiController(BattleRuntime runtime, Faction playerFaction, Func<bool> playbackBusy)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(playerFaction);
    ArgumentNullException.ThrowIfNull(playbackBusy);
    _runtime = runtime;
    _playerFaction = playerFaction;
    _playbackBusy = playbackBusy;
    // The factory finishes bootstrap before returning, so an already-decided outcome never
    // arrives through ActionCompleted; initialize terminal state from the current outcome.
    _state = runtime.Query(new GetBattleOutcomeQuery()).IsSome
      ? UiState.BattleOver
      : UiState.Unselected;
    _runtime.ActionCompleted += OnActionResult;
  }

  public UiState State => _state;

  // Derived on demand, never stored: playback busy beats everything, then turn/phase facts.
  public InputGate Gate
  {
    get
    {
      if (_disposed) return InputGate.NotPlayerTurn;
      if (_playbackBusy())
        return InputGate.PlaybackBusy;
      if (_runtime.Query(new GetBattleOutcomeQuery()).IsSome)
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

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _runtime.ActionCompleted -= OnActionResult;
    _optionsByList.Clear();
    _options = [];
    _selected = None;
    ResetTargeting();
  }

  public bool TrySelectAt(Vector3I tile)
  {
    if (Gate != InputGate.Open || _state == UiState.BattleOver)
      return false;

    return _runtime.TryGetTile(tile).Bind(point => _runtime.Query(new GetUnitAtTile(point))).Match(
      Some: unit =>
      {
        if (!ReferenceEquals(unit.Side, _playerFaction) || unit.IsUnconscious)
          return false;
        ResetTargeting();
        _selected = Some(unit);
        RefreshOptions();
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
        SetState(UiState.Unselected);
        break;
    }
  }

  public void BeginAction(UnitActionOption option)
  {
    ArgumentNullException.ThrowIfNull(option);
    if (Gate != InputGate.Open || _state != UiState.UnitSelected)
      return;
    if (!_selected.Contains(option.Unit) || !option.IsAvailable)
      return;

    switch (option)
    {
      case INeedsTargeting targeted:
        IActionTargeting handler = targeted.Targeting(_runtime);
        _targeter = Some(handler);
        handler.Begin();
        _pendingTarget = None;
        _lastPreview = None;
        SetState(UiState.Targeting);
        break;
      case CanActDirectly instant:
        // OnActionResult (via the signal) refreshes options synchronously.
        _runtime.ExecuteAction(instant.MakeAction(_runtime));
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
      case UiState.TargetingLocked:
        {
          IActionTargeting handler = RequireHandler();
          if (_state == UiState.TargetingLocked && _pendingTarget == Some(tile))
            return Confirm();
          if (!handler.CanCommit(tile))
            return false;
          if (handler.Confirm == ConfirmMode.Immediate)
          {
            _runtime.ExecuteAction(handler.Build(tile));
            return true; // OnActionResult resets targeting and lands us in UnitSelected
          }
          bool firstTarget = _state == UiState.Targeting;
          _pendingTarget = Some(tile);
          _lastPreview = handler.Preview(tile).Match(
            Right: preview => Some(preview),
            Left: _ => None);
          SetState(UiState.TargetingLocked);
          return firstTarget;
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

  public void ClearHoverPreview()
  {
    if (Gate == InputGate.Open && _state == UiState.Targeting)
      _lastPreview = None;
  }

  // The single submission-caused transition point. Runs synchronously inside
  // runtime.ExecuteAction, after the submission's state mutations are committed.
  private void OnActionResult(BattleActionExecResult result)
  {
    if (_disposed || _state == UiState.BattleOver)
      return;

    bool battleOver = _runtime.Query(new GetBattleOutcomeQuery()).IsSome;
    _selected = battleOver ? None : _selected.Bind(_runtime.TryGetAlive).Map(alive => alive.State)
      .Filter(unit => !unit.IsUnconscious);
    ResetTargeting();
    RefreshOptions();
    SetState(battleOver ? UiState.BattleOver
      : _selected.IsSome ? UiState.UnitSelected : UiState.Unselected);
  }

  private void RefreshOptions()
  {
    // Mint the aliveness proof at the query door; a dead selected unit yields empty options.
    _options = _selected.Bind(_runtime.TryGetAlive).Match(
      Some: alive => GetOrBuildOptions(_runtime.Query(new GetAvailableActionsForUnit(alive))),
      None: []);
  }

  private IReadOnlyList<UnitActionOption> GetOrBuildOptions(IReadOnlyList<UnitAction> actions)
  {
    if (_optionsByList.TryGetValue(actions, out var options))
      return options;

    options = BuildOptions(actions);
    _optionsByList.Add(actions, options);
    return options;
  }

  private static System.Collections.ObjectModel.ReadOnlyCollection<UnitActionOption> BuildOptions(IReadOnlyList<UnitAction> actions) =>
    actions.AsValueEnumerable().Select(MakeOption).ToList().AsReadOnly();

  private static UnitActionOption MakeOption(UnitAction action) => action.Action switch
  {
    MoveActionDefinition => new MoveActionOption(action),
    AttackActionDefinition => new AttackActionOption(action),
    ReloadActionDefinition => new ReloadActionOption(action),
    PassActionDefinition => new PassActionOption(action),
    EndTurnActionDefinition => new EndTurnActionOption(action),
    _ => throw new InvalidOperationException($"No presentation option for {action.Action.GetType().Name}."),
  };

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
    _state = next;
    Changed.Invoke();
  }
}
