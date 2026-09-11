using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using Godot;

// Base class of every geoscape screen. The shared Background/Viewport shell and each
// screen's foreground are authored in scenes. Views request navigation through Godot
// signals; Present is the composition root's single binding/refresh entry point.
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
    if (!HidesPreviousScene)
      GetNode<SubViewportContainer>("Background").Visible = false;
  }
}
