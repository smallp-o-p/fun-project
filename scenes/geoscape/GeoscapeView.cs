using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using Godot;

/// <summary>
/// Base class for a screen in the Geoscape. Must implement a way to go back.
/// This also accommodates "transparent" views e.g. modal dialogs.
/// </summary>
public partial class GeoscapeView : Control
{
  [Export] public BaseButton? BackButton { get; set; }
  [Export] public bool HidesPreviousScene { get; set; } = true;

  [Signal] public delegate void ViewRequestedEventHandler(GeoscapeView view);
  [Signal] public delegate void BackRequestedEventHandler();

  public void RequestView(GeoscapeView view) => EmitSignal(SignalName.ViewRequested, view);
  public void RequestBack() => EmitSignal(SignalName.BackRequested);

  public virtual void Present(CampaignGameState state, GeoscapeSession session) { }

  public override void _Ready()
  {
    if (BackButton is not null)
      BackButton.Pressed += RequestBack;
  }
}
