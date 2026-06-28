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
  private IReadOnlyList<UnitActionOption> _options = [];
  private Option<IActionTargeting> _currentActionTargeter;
  private Option<Vector3I> _pendingTarget;
  private Option<ActionPreview> _lastPreview;

  public Option<BattleUnitState> SelectedUnit { get; private set; }
  public TargetingMode Mode { get; private set; } = TargetingMode.None;
  public IReadOnlyList<UnitActionOption> ActionOptions => _options;

  // The active targeting handler owns its candidate set; render from it directly (no local duplicate).
  public IReadOnlyCollection<Vector3I> CandidateCells =>
    _currentActionTargeter.Match(
      Some: handler => handler.Candidates,
      None: () => System.Array.Empty<Vector3I>());

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
    handler.Begin(); // populates the handler's own candidate set, which CandidateCells reads from
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
        Right: availability => BuildOptions(unit, availability),
        Left: []),
      None: []);
  }

  // Presentation-side mapping: turn the domain availability fact into concrete verb options. Each verb
  // takes its availability straight from the fact (single source) and supplies its own targeting handler.
  // Attack is included only when a weapon is equipped (matching the verb's capability requirement).
  private static IReadOnlyList<UnitActionOption> BuildOptions(BattleUnitState unit, UnitActionAvailability availability)
  {
    var options = new List<UnitActionOption>
    {
      new MoveActionOption(unit, availability.CanMove),
    };

    unit.EquippedWeapon.IfSome(weapon =>
      options.Add(new AttackActionOption(unit, weapon, availability.CanAttack)));

    options.Add(new PassActionOption(unit, availability.CanPass));
    options.Add(new EndTurnActionOption(unit, availability.CanEndTurn));
    return options;
  }

  private void ResetTargeting()
  {
    _currentActionTargeter = None;
    _pendingTarget = None;
    _lastPreview = None;
    Mode = TargetingMode.None;
  }

  private IActionTargeting RequireHandler() =>
    _currentActionTargeter.Match(handler => handler, () => throw new InvalidOperationException("No active targeting handler."));
}
