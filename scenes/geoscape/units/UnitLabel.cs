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

  public void Press() => Pressed?.Invoke();

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
    GetNode<Button>("%ClickTarget").Pressed += Press;
  }
}
