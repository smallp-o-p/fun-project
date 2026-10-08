using FunProject.Battle;
using Godot;
using System.Collections.Generic;

/// <summary>
/// Runtime-free targeting overlays. The authored HUD owns its widgets and intent events.
/// </summary>
public sealed partial class BattleUI : Node
{
  public BattleHud Hud { get; private set; } = null!;
  private MovementLine _movementLine = null!;
  private ReachableTileHighlighter _highlighter = null!;

  public override void _Ready()
  {
    Hud = GetNode<BattleHud>("%BattleHud");
    _movementLine = GetNode<MovementLine>("%MovementLine");
    _highlighter = GetNode<ReachableTileHighlighter>("%ReachableTileHighlighter");
  }

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
        Hud.HideHitChance();
        DrawLine(p.Path);
        break;
      case AttackPreview a:
        _movementLine.Visible = false;
        Hud.ShowHitChance(a.HitChance.FinalChance);
        break;
    }
  }

  public void HidePreview()
  {
    _movementLine.Visible = false;
    Hud.HideHitChance();
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
