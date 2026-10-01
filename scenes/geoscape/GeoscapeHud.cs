using FunProject.Engineering;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;

/// <summary>
/// Geoscape HUD controls. GeoscapeScene wires callbacks into the buttons and pushes state updates to be presented.
/// </summary>
public sealed partial class GeoscapeHud : Control
{
  private Label _clockLabel = null!;
  private Button _pauseButton = null!;
  private Button _speedButton = null!;
  private VBoxContainer _alerts = null!;
  private Label _engineeringProgress = null!;
  private Label _engineeringNotice = null!;
  private readonly SysColGeneric.List<(Button Button, GeoscapeEvent Event)> _alertButtons = [];

  private TimeSpeed _lastActiveSpeed = TimeSpeed.Normal;

  public event Action<TimeSpeed>? ChangeSpeed;
  public event Action<GeoscapeEvent>? ResolutionRequested;

  [Signal] public delegate void ViewRequestedEventHandler(GeoscapeView view);

  public override void _Ready()
  {
    _speedButton = GetNode<Button>("%SpeedButton");
    _pauseButton = GetNode<Button>("%PauseButton");
    _clockLabel = GetNode<Label>("%ClockLabel");
    _alerts = GetNode<VBoxContainer>("%Alerts");
    _engineeringProgress = GetNode<Label>("%EngineeringProgress");
    _engineeringNotice = GetNode<Label>("%EngineeringNotice");

    _speedButton.Pressed += UpdateSpeed;
    _pauseButton.Toggled += pressed =>
      ChangeSpeed?.Invoke(pressed ? TimeSpeed.Paused : _lastActiveSpeed);
  }

  public void RequestView(PackedScene scene)
  {
    EmitSignal(SignalName.ViewRequested, scene.InstantiateAs<GeoscapeView>());
  }

  public void UpdateManufacturing(Option<ManufacturingJob> manufacturing, long tick)
  {
    _engineeringProgress.Text = manufacturing.Match(
      job => $"Manufacturing: {job.Project.Item.Name} — {ProjectTimeText.Remaining(job.CompletesAtTick, tick)} remaining",
      () => "No active manufacturing.");
  }

  public void ShowManufacturingCompleted(ManufacturingJob job)
    => _engineeringNotice.Text = $"Manufacturing completed: {job.Project.Item.Name}";

  private static string Speed2Text(TimeSpeed speed)
  {
    return speed switch
    {
      TimeSpeed.Normal => "1x",
      TimeSpeed.Fast => "5x",
      TimeSpeed.VeryFast => "12.5x",
      TimeSpeed.VeryVeryFast => "25x",
      TimeSpeed.Paused => "0x",
      _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null)
    };
  }

  private void UpdateSpeed()
  {
    _lastActiveSpeed = _lastActiveSpeed switch
    {
      TimeSpeed.Normal => TimeSpeed.Fast,
      TimeSpeed.Fast => TimeSpeed.VeryFast,
      TimeSpeed.VeryFast => TimeSpeed.VeryVeryFast,
      _ => TimeSpeed.Normal,
    };

    _pauseButton.SetPressedNoSignal(false); // selecting a speed implies running
    _speedButton.Text = Speed2Text(_lastActiveSpeed);
    ChangeSpeed?.Invoke(_lastActiveSpeed);
  }

  public void UpdateClock(int day, DateTime newTime)
  {
    _clockLabel.Text = $"Day {day} {newTime:HH:mm}";
  }

  // Rebuild alert list when one of them changes
  public void RefreshAlerts(SysColGeneric.IReadOnlyList<GeoscapeEvent> activeEvents)
  {
    _alerts.QueueFreeAllChildren();
    _alertButtons.Clear();

    foreach (GeoscapeEvent active in activeEvents)
    {
      var button = new Button
      {
        CustomMinimumSize = new Vector2(0, 56),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        Alignment = HorizontalAlignment.Left,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
      };
      button.Pressed += () => ResolutionRequested?.Invoke(active);
      _alerts.AddChild(button);
      _alertButtons.Add((button, active));
    }
  }

  public void UpdateCountdowns(long currentTick)
  {
    foreach ((Button button, GeoscapeEvent active) in _alertButtons)
    {
      string countdown = active.ExpiresAtTick.Match(
        expiresAt => $" ({Math.Max(0L, expiresAt - currentTick) * GeoscapeSession.TickGameSeconds / 60} min)",
        () => "");
      button.Text = $"[{active.Definition.Kind}]\n{active.Definition.Title}{countdown}";
    }
  }
}
