using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Runtime-free battle view: owns the HUD widgets, the movement line, and the reachable-tile
/// highlighter. BattleScene pushes view models in to be presented; intents flow back out
/// through events.
/// </summary>
public sealed partial class BattleUI : Node
{
  private BattleHud _hud = null!;
  private MovementLine _movementLine = null!;
  private ReachableTileHighlighter _highlighter = null!;

  public event Action? ConfirmRequested;
  public event Action? CancelRequested;
  public event Action<UnitActionOption>? VerbSelected;
  public event Action? ReturnRequested;

  public override void _Ready()
  {
    _hud = GetNode<BattleHud>("%BattleHud");
    _movementLine = GetNode<MovementLine>("%MovementLine");
    _highlighter = GetNode<ReachableTileHighlighter>("%ReachableTileHighlighter");

    _hud.ConfirmRequested += () => ConfirmRequested?.Invoke();
    _hud.CancelRequested += () => CancelRequested?.Invoke();
    _hud.VerbSelected += option => VerbSelected?.Invoke(option);
    _hud.ReturnRequested += () => ReturnRequested?.Invoke();
  }

  public void ShowUnits(IReadOnlyList<BattleUnitState> units)
    => _hud.ShowUnits(units);

  public void ShowActionOptions(IReadOnlyList<UnitActionOption> options)
    => _hud.ShowActionOptions(options);

  public void ShowBattleOver(string bannerText)
    => _hud.ShowBattleOver(bannerText);

  public void ShowReturn(bool available)
    => _hud.ShowReturn(available);

  public void ShowTargeting(IReadOnlyCollection<Vector3I> candidateCells, Option<ActionPreview> preview)
  {
    _highlighter.Show(candidateCells);
    preview.Match(ShowPreview, HidePreview);
  }

  public void HideTargeting()
  {
    _highlighter.Clear();
    HidePreview();
  }

  public void ShowPreview(ActionPreview preview)
  {
    switch (preview)
    {
      case PathPreview p:
        _hud.HideHitChance();
        DrawLine(p.Path);
        break;
      case AttackPreview a:
        _movementLine.Visible = false;
        _hud.ShowHitChance(a.HitChance.FinalChance);
        break;
    }
  }

  public void HidePreview()
  {
    _movementLine.Visible = false;
    _hud.HideHitChance();
  }

  private void DrawLine(IReadOnlyList<Vector3I> path)
  {
    if (path.Count < 2)
    {
      _movementLine.Visible = false;
      return;
    }

    _movementLine.ShowPath(path.AsValueEnumerable()
      .Select(tile => BoardCoordinates.TileToWorldCenter(tile) + new Vector3(0f, 0.05f, 0f))
      .ToArray());
  }
}
