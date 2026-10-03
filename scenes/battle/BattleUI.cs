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
  public BoardCoordinates Coordinates { get; set; } = BoardCoordinates.UnitGrid;

  private BattleHud _hud = null!;
  private MovementLine _movementLine = null!;
  private ReachableTileHighlighter _highlighter = null!;
  private float _lineWidth;
  private float _cornerRadius;

  public event Action? ConfirmRequested;
  public event Action? CancelRequested;
  public event Action<UnitActionOption>? VerbSelected;

  public override void _Ready()
  {
    _hud = GetNode<BattleHud>("%BattleHud");
    _movementLine = GetNode<MovementLine>("%MovementLine");
    _lineWidth = _movementLine.Width;
    _cornerRadius = _movementLine.CornerRadius;
    _highlighter = GetNode<ReachableTileHighlighter>("%ReachableTileHighlighter");

    _hud.ConfirmRequested += () => ConfirmRequested?.Invoke();
    _hud.CancelRequested += () => CancelRequested?.Invoke();
    _hud.VerbSelected += option => VerbSelected?.Invoke(option);
  }

  public void ShowUnits(IReadOnlyList<BattleUnitState> units)
    => _hud.ShowUnits(units);

  public void ShowActionOptions(IReadOnlyList<UnitActionOption> options)
    => _hud.ShowActionOptions(options);

  public void ShowBattleOver(string bannerText)
    => _hud.ShowBattleOver(bannerText);

  public void ShowTargeting(IReadOnlyCollection<Vector3I> candidateCells, Option<ActionPreview> preview)
  {
    _highlighter.Coordinates = Coordinates;
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

    _movementLine.Width = _lineWidth * Coordinates.CellSize.X;
    _movementLine.CornerRadius = _cornerRadius * Coordinates.CellSize.X;
    _movementLine.ShowPath(path.AsValueEnumerable()
      .Select(tile => _movementLine.ToLocal(Coordinates.TileToWorldCenter(tile) + Coordinates.Up * (0.05f * Coordinates.CellSize.Y)))
      .ToArray());
  }
}
