using FunProject.Combatants;
using Godot;
using System;

public partial class UnitLabel : PanelContainer
{
  public RichTextLabel? UnitName { get; private set; }
  public RichTextLabel? RankName { get; private set; }
  public RichTextLabel? Status { get; private set; }
  public TextureRect? UnitIcon { get; private set; }
  public TextureRect? RankIcon { get; private set; }
  public event Action? Pressed;

  /// <summary>Programmatic press (future keyboard nav); the ClickTarget button routes mouse clicks here.</summary>
  public void Press() => Pressed?.Invoke();

  // Status has no domain concept yet; the view owns the placeholder. Rank comes live from
  // the combatant's rank ladder (UnitRank).
  private const string StatusPlaceholder = "Ready";

  public void Bind(Combatant unit)
  {
    UnitName!.Text = unit.Name;
    RankName!.Text = unit.Rank.RankName;
    Status!.Text = StatusPlaceholder;
  }

  public override void _Ready()
  {
    UnitName = GetNode<RichTextLabel>("%Name");
    RankName = GetNode<RichTextLabel>("%RankName");
    Status = GetNode<RichTextLabel>("%Status");
    UnitIcon = GetNode<TextureRect>("%UnitIcon");
    RankIcon = GetNode<TextureRect>("%RankIcon");

    // Full-rect flat button drawn above the content (last child): any click inside the
    // row lands here regardless of the display children's own mouse filters.
    GetNode<Button>("%ClickTarget").Pressed += Press;
  }
}
