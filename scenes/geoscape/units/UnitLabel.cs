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

  /// <summary>Programmatic press (tests + future keyboard nav); _GuiInput routes mouse clicks here.</summary>
  public void Press() => Pressed?.Invoke();

  public override void _GuiInput(InputEvent @event)
  {
    if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
      Press();
  }

  // Rank/status have no domain concepts yet (progression / assignment FSM are future
  // systems); the view owns the placeholder strings until those land.
  private const string RankPlaceholder = "—";
  private const string StatusPlaceholder = "Ready";

  public void Bind(Combatant unit)
  {
    UnitName!.Text = unit.Name;
    RankName!.Text = RankPlaceholder;
    Status!.Text = StatusPlaceholder;
  }

  public override void _Ready()
  {
    UnitName = GetNode<RichTextLabel>("%Name");
    RankName = GetNode<RichTextLabel>("%RankName");
    Status = GetNode<RichTextLabel>("%Status");
    UnitIcon = GetNode<TextureRect>("%UnitIcon");
    RankIcon = GetNode<TextureRect>("%RankIcon");

    foreach (Node node in FindChildren("*", "Control", true, false))
      ((Control)node).MouseFilter = MouseFilterEnum.Ignore;
  }
}
