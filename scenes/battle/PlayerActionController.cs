using FunProject;
using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;

// Presentation-side input/targeting controller. Caches the selected unit's available action options,
// holds the active per-verb targeting handler, and drives the None -> ActionTargeting -> ActionPending
// machine. Instant verbs (Pass/EndTurn) are built and submitted inline. Stays Direct: every commit is
// a real BattleAction; previews come only from real queries.
public sealed class PlayerActionController
{
  public enum TargetingMode { None, ActionTargeting, ActionPending }

  private readonly BattleRuntime _runtime;
  private readonly Faction _playerFaction;
  private readonly SysColGeneric.HashSet<Vector3I> _candidateCells = [];
  private IReadOnlyList<UnitActionOption> _options = [];
  private Option<IActionTargeting> _currentActionTargeter;
  private Option<Vector3I> _pendingTarget;
  private Option<ActionPreview> _lastPreview;

  public Option<BattleUnitState> SelectedUnit { get; private set; }
  public TargetingMode Mode { get; private set; } = TargetingMode.None;
  public IReadOnlyList<UnitActionOption> ActionOptions => _options;
  public IReadOnlyCollection<Vector3I> CandidateCells => _candidateCells;
  public Option<Vector3I> PendingTarget => _pendingTarget;
  public Option<ActionPreview> LastPreview => _lastPreview;

  public PlayerActionController(BattleRuntime runtime, Faction playerFaction)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(playerFaction);
    _runtime = runtime;
    _playerFaction = playerFaction;
  }

  // Selects the controllable (player-faction) unit on the tile and caches its action options.
  public bool TrySelectUnitAt(Vector3I tile)
  {
    return _runtime.Query(new GetUnitAtTile(tile)).Match(
      Right: occupant => occupant.Match(
        Some: unit =>
        {
          if (unit.Side != _playerFaction)
            return false;
          SelectedUnit = Some(unit);
          ResetTargeting();
          RefreshOptions();
          return true;
        },
        None: () => false),
      Left: _ => false);
  }

  public void BeginAction(UnitActionOption option)
  {
    ArgumentNullException.ThrowIfNull(option);

    SelectedUnit.IfSome(unit =>
    {
      if (option is NeedsTargeting target)
      {
        StartTargeting(target.Targeting(_runtime));
      }
      else if (option is CanActDirectly actionNow)
      {
        Submit(actionNow.MakeAction());
      }
      else
      {
        throw new InvalidOperationException($"Unhandled action option {option.GetType().Name}.");
      }
    });
  }

  public Either<BattleQueryFailure, ActionPreview> PreviewAt(Vector3I tile)
  {
    Either<BattleQueryFailure, ActionPreview> result = RequireHandler().Preview(tile);
    _lastPreview = result.Match(
      Right: preview => preview,
      Left: _ => default(ActionPreview));
    return result;
  }

  public bool SetPending(Vector3I tile)
  {
    if (Mode is not (TargetingMode.ActionTargeting or TargetingMode.ActionPending))
      return false;
    if (!RequireHandler().CanCommit(tile))
      return false;

    _pendingTarget = Some(tile);
    Mode = TargetingMode.ActionPending;
    return true;
  }

  public IReadOnlyList<BattleActionResult> Confirm()
  {
    if (Mode != TargetingMode.ActionPending)
      return [];

    IActionTargeting handler = RequireHandler();
    Vector3I target = _pendingTarget.Match(t => t, () => throw new InvalidOperationException("No pending target."));
    return Submit(handler.Build(target));
  }

  // Steps back one level: ActionPending -> ActionTargeting -> None (deselects from None/Selected).
  public void Cancel()
  {
    switch (Mode)
    {
      case TargetingMode.ActionPending:
        _pendingTarget = None;
        _lastPreview = None;
        Mode = TargetingMode.ActionTargeting;
        break;
      case TargetingMode.ActionTargeting:
        ResetTargeting();
        break;
      default:
        SelectedUnit = None;
        _options = [];
        ResetTargeting();
        break;
    }
  }

  private void StartTargeting(IActionTargeting handler)
  {
    _currentActionTargeter = Some(handler);
    _candidateCells.Clear();
    foreach (Vector3I cell in handler.Begin())
      _candidateCells.Add(cell);
    _pendingTarget = None;
    _lastPreview = None;
    Mode = TargetingMode.ActionTargeting;
  }

  private IReadOnlyList<BattleActionResult> Submit(BattleAction action)
  {
    IReadOnlyList<BattleActionResult> results = _runtime.ExecuteAction(action);
    ResetTargeting();
    RefreshOptions();
    return results;
  }

  private void RefreshOptions()
  {
    _options = SelectedUnit.Match(
      Some: unit => _runtime.Query(new GetUnitActionOptions(unit)).Match(
        Right: o => o,
        Left: []),
      None: []);
  }

  private void ResetTargeting()
  {
    _currentActionTargeter = None;
    _candidateCells.Clear();
    _pendingTarget = None;
    _lastPreview = None;
    Mode = TargetingMode.None;
  }

  private IActionTargeting RequireHandler() =>
    _currentActionTargeter.Match(handler => handler, () => throw new InvalidOperationException("No active targeting handler."));
}
