using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;

public enum UiState { Unselected, UnitSelected, Targeting, TargetingLocked, BattleOver }
public enum InputGate { Open, PlaybackBusy, NotPlayerTurn, RuntimeBusy, RuntimeClosed }

// Host-thread presentation state. Live reads use lexical scopes; retained HUD values
// remain safe while busy. Submission-caused transitions belong to OnActionResult.
public sealed class BattleUiController : IDisposable
{
  private readonly BattleRuntime _runtime;
  private readonly Faction _playerFaction;
  private readonly Func<bool> _playbackBusy;
  private UiState _state;
  private Option<BattleUnitState> _selected;
  private readonly Dictionary<IReadOnlyList<UnitAction>, IReadOnlyList<UnitActionOption>> _optionsByList = [];
  private IReadOnlyList<UnitActionOption> _options = [];
  private IReadOnlyList<UnitReadout> _units = [];
  private IReadOnlyList<ActionOptionReadout> _actionReadouts = [];
  private Option<IActionTargeting> _targeter;
  private Option<Vector3I> _pendingTarget;
  private Option<ActionPreview> _lastPreview;
  private Option<Vector3I> _pendingHover;
  private bool _refreshPending;
  private bool _disposed;

  public event Action StateChanged = delegate { };
  public event Action ReadoutsChanged = delegate { };

  public BattleUiController(BattleRuntime runtime, Faction playerFaction, Func<bool> playbackBusy)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(playerFaction);
    ArgumentNullException.ThrowIfNull(playbackBusy);
    _runtime = runtime; _playerFaction = playerFaction; _playbackBusy = playbackBusy;
    using var scope = runtime.TryAcquireQueryScope().Match(Right: value => value, Left: failure => throw new InvalidOperationException($"Battle access failed: {failure}."));
    _state = runtime.Query(new GetBattleOutcomeQuery()).IsSome ? UiState.BattleOver : UiState.Unselected;
    RefreshReadouts();
    _runtime.ActionCompleted += OnActionResult;
  }

  public UiState State => _state;
  public Option<BattleUnitState> SelectedUnit => _selected;
  public IReadOnlyList<UnitActionOption> ActionOptions => _options;
  public IReadOnlyList<UnitReadout> UnitReadouts => _units;
  public IReadOnlyList<ActionOptionReadout> ActionReadouts => _actionReadouts;
  public Option<ActionPreview> LastPreview => _lastPreview;
  public Option<Vector3I> PendingTarget => _pendingTarget;
  public IReadOnlyCollection<Vector3I> CandidateCells => _targeter.Match(handler => handler.Candidates, () => System.Array.Empty<Vector3I>());

  public InputGate Gate
  {
    get
    {
      if (!TryRead(out var scope)) return _disposed || _runtime.IsClosing ? InputGate.RuntimeClosed : InputGate.RuntimeBusy;
      using (scope) return GateOwned;
    }
  }
  private InputGate GateOwned => _playbackBusy() ? InputGate.PlaybackBusy
    : _runtime.Query(new GetBattleOutcomeQuery()).IsSome || _runtime.Query(new GetActiveSideQuery()) != _playerFaction
      ? InputGate.NotPlayerTurn : InputGate.Open;

  private bool TryRead(out BattleQueryScope? scope)
  {
    scope = null;
    if (_disposed) return false;
    var admission = _runtime.TryAcquireQueryScope();
    if (admission.IsRight) { scope = admission.Match(Right: value => value, Left: failure => throw new InvalidOperationException($"Battle access failed: {failure}.")); return true; }
    if (admission.Match(Left: failure => failure, Right: _ => throw new InvalidOperationException("Expected failed battle admission.")) == BattleAccessFailure.Closed) ClearDeferred();
    else _refreshPending = true;
    return false;
  }

  private readonly record struct Intent(bool Handled, Func<BattleRuntime, BattleAction>? Submit = null);
  private bool RunIntent(Func<Intent> inspect)
  {
    if (!TryRead(out var scope)) return false;
    Intent intent;
    long revision;
    using (scope)
    {
      if (GateOwned != InputGate.Open || _state == UiState.BattleOver) return false;
      revision = scope!.Revision;
      intent = inspect();
    }
    if (intent.Submit is null) return intent.Handled;
    var result = _runtime.TryExecuteAction(intent.Submit, Some(revision));
    if (result.IsRight) return intent.Handled;
    if (result.Match(Left: failure => failure, Right: _ => throw new InvalidOperationException("Expected failed battle admission.")) == BattleAccessFailure.Closed) ClearDeferred();
    else _refreshPending = true;
    return false;
  }

  public bool TrySelectAt(Vector3I tile) => RunIntent(() => new(SelectOwned(tile)));
  private bool SelectOwned(Vector3I tile) => _runtime.TryGetTile(tile).Bind(point => _runtime.Query(new GetUnitAtTile(point))).Match(
    unit =>
    {
      if (unit.Side != _playerFaction || unit.IsUnconscious) return false;
      ResetTargeting(); _selected = Some(unit); RefreshReadouts();
      ReadoutsChanged.Invoke(); SetState(UiState.UnitSelected); return true;
    }, () => false);

  public void Cancel() => RunIntent(() =>
  {
    switch (_state)
    {
      case UiState.TargetingLocked:
        _pendingTarget = None; _lastPreview = None; SetState(UiState.Targeting); break;
      case UiState.Targeting:
        ResetTargeting(); SetState(UiState.UnitSelected); break;
      default:
        ResetTargeting(); _selected = None; RefreshReadouts(); ReadoutsChanged.Invoke(); SetState(UiState.Unselected); break;
    }
    return new(true);
  });

  public void BeginAction(UnitActionOption option)
  {
    ArgumentNullException.ThrowIfNull(option);
    RunIntent(() =>
    {
      if (_state != UiState.UnitSelected || !_selected.Contains(option.Unit) || !option.IsAvailable) return new(false);
      if (option is NeedsTargeting targeted)
      {
        var handler = targeted.Targeting(_runtime); _targeter = Some(handler); handler.Begin();
        _pendingTarget = None; _lastPreview = None; SetState(UiState.Targeting); return new(true);
      }
      if (option is CanActDirectly instant) return new(true, runtime =>
      {
        if (!option.IsAvailable) throw new InvalidOperationException("Action became unavailable at submission.");
        return instant.MakeAction(runtime);
      });
      throw new InvalidOperationException($"Unhandled action option {option.GetType().Name}.");
    });
  }

  private Intent SubmitTarget(Vector3I tile)
  {
    var handler = RequireHandler();
    return new(true, _ =>
    {
      handler.Begin();
      if (!handler.CanCommit(tile)) throw new InvalidOperationException("Target became unavailable at submission.");
      return handler.Build(tile);
    });
  }

  public bool ClickTile(Vector3I tile) => RunIntent(() =>
  {
    switch (_state)
    {
      case UiState.Unselected:
      case UiState.UnitSelected: return new(SelectOwned(tile));
      case UiState.Targeting:
        var handler = RequireHandler();
        if (!handler.CanCommit(tile)) return new(false);
        if (handler.Confirm == ConfirmMode.Immediate) return SubmitTarget(tile);
        _pendingTarget = Some(tile); PreviewOwned(tile); SetState(UiState.TargetingLocked); return new(true);
      case UiState.TargetingLocked:
        if (_pendingTarget == Some(tile)) return SubmitTarget(tile);
        if (RequireHandler().CanCommit(tile)) { _pendingTarget = Some(tile); PreviewOwned(tile); }
        return new(false);
      default: return new(false);
    }
  });
  public bool Confirm() => RunIntent(() => _state == UiState.TargetingLocked
    ? SubmitTarget(_pendingTarget.Match(value => value, () => throw new InvalidOperationException("No pending target."))) : new(false));

  private static Either<BattleQueryFailure, ActionPreview> UnavailablePreview =>
    Left(new BattleQueryFailure(BattleQueryFailureReason.InvalidBattleState, "UI is not targeting or input is gated."));
  public Either<BattleQueryFailure, ActionPreview> PreviewAt(Vector3I tile)
  {
    if (!TryRead(out var scope))
    {
      if (!_disposed && !_runtime.IsClosing && _state == UiState.Targeting) _pendingHover = Some(tile);
      return UnavailablePreview;
    }
    using (scope)
    {
      if (GateOwned != InputGate.Open || _state != UiState.Targeting) return UnavailablePreview;
      return PreviewOwned(tile);
    }
  }
  private Either<BattleQueryFailure, ActionPreview> PreviewOwned(Vector3I tile)
  {
    var handler = RequireHandler();
    if (!handler.CanCommit(tile)) { _lastPreview = None; return UnavailablePreview; }
    var result = handler.Preview(tile);
    _lastPreview = result.Match(Right: preview => Some(preview), Left: _ => None);
    return result;
  }

  public void TickDeferredRefresh()
  {
    if (_disposed || (!_refreshPending && _pendingHover.IsNone)) return;
    if (!TryRead(out var scope)) return;
    using (scope)
    {
      _refreshPending = false;
      RefreshReadouts();
      _targeter.IfSome(handler => handler.Begin());
      // Retry after refresh, then notify once so the latest hover renders last.
      if (GateOwned == InputGate.Open && _state == UiState.Targeting)
        _pendingHover.IfSome(tile => PreviewOwned(tile));
      _pendingHover = None;
      ReadoutsChanged.Invoke();
    }
  }

  private void OnActionResult(BattleActionExecResult result)
  {
    if (_disposed || _state == UiState.BattleOver) return;
    if (!TryRead(out var scope)) return;
    using (scope)
    {
      _refreshPending = false;
      if (_runtime.Query(new GetBattleOutcomeQuery()).IsSome)
      {
        ResetTargeting(); _selected = None; RefreshReadouts(); ReadoutsChanged.Invoke(); SetState(UiState.BattleOver); return;
      }
      _selected = _selected.Bind(_runtime.TryGetAlive).Map(alive => alive.State).Filter(unit => !unit.IsUnconscious);
      if (_selected.IsNone)
      {
        ResetTargeting(); RefreshReadouts(); ReadoutsChanged.Invoke(); SetState(UiState.Unselected); return;
      }
      if (_state is UiState.Targeting or UiState.TargetingLocked) { ResetTargeting(); SetState(UiState.UnitSelected); }
      RefreshReadouts(); ReadoutsChanged.Invoke();
    }
  }

  private void RefreshReadouts()
  {
    _units = System.Array.AsReadOnly(_runtime.Query(new GetFactionAliveUnits(_playerFaction)).AsValueEnumerable()
      .Select(unit => UnitReadout.Capture(unit.State)).ToArray());
    _options = _selected.Bind(_runtime.TryGetAlive).Match(alive => GetOrBuildOptions(_runtime.Query(new GetAvailableActionsForUnit(alive))), () => []);
    _actionReadouts = System.Array.AsReadOnly(_options.AsValueEnumerable().Select(option => new ActionOptionReadout(option, option.IsAvailable)).ToArray());
  }
  private IReadOnlyList<UnitActionOption> GetOrBuildOptions(IReadOnlyList<UnitAction> actions)
  {
    if (_optionsByList.TryGetValue(actions, out var options)) return options;
    options = actions.AsValueEnumerable().Select(MakeOption).ToList().AsReadOnly();
    _optionsByList.Add(actions, options); return options;
  }
  private static UnitActionOption MakeOption(UnitAction action) => action.Action switch
  {
    MoveActionDefinition => new MoveActionOption(action),
    AttackActionDefinition => new AttackActionOption(action),
    ReloadActionDefinition => new ReloadActionOption(action),
    PassActionDefinition => new PassActionOption(action),
    EndTurnActionDefinition => new EndTurnActionOption(action),
    _ => throw new InvalidOperationException($"No presentation option for {action.Action.GetType().Name}.")
  };
  private void ResetTargeting() { _targeter = None; _pendingTarget = None; _lastPreview = None; _pendingHover = None; }
  private void ClearDeferred() { _refreshPending = false; _pendingHover = None; }
  private IActionTargeting RequireHandler() => _targeter.Match(value => value, () => throw new InvalidOperationException("No active targeter."));
  private void SetState(UiState next) { if (_state == next) return; _state = next; StateChanged.Invoke(); }
  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true; _runtime.ActionCompleted -= OnActionResult;
    _optionsByList.Clear(); _options = []; _units = []; _actionReadouts = []; _selected = None;
    ResetTargeting(); ClearDeferred();
  }
}
