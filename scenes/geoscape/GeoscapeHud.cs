using FunProject.Engineering;
using FunProject.Strategic;
using Godot;
using System;

// Authored top bar, left alert list and right project status panel. Pure presentation:
// state arrives via pushes; requests flow out as C# events routed by the composition root.
// The completion notice persists until replaced by the next completion.
public sealed partial class GeoscapeHud : CanvasLayer
{
  private Label _clockLabel = null!;
  private Button _pauseButton = null!;
  private Button _speedButton = null!;
  private Button _unitsButton = null!;
  private VBoxContainer _alerts = null!;
  private Label _engineeringProgress = null!;
  private Label _engineeringNotice = null!;
  private readonly SysColGeneric.List<(Button Button, GeoscapeEvent Event)> _alertButtons = [];

  private TimeSpeed _lastActiveSpeed = TimeSpeed.Normal;

  public event Action<TimeSpeed>? ChangeSpeed;
  public event Action<GeoscapeEvent>? ResolutionRequested;
  public event Action<GeoscapeView>? ViewRequested;

  public override void _Ready()
  {
    _speedButton = GetNode<Button>("%SpeedButton");
    _unitsButton = GetNode<Button>("%UnitsButton");
    _pauseButton = GetNode<Button>("%PauseButton");
    _clockLabel = GetNode<Label>("%ClockLabel");
    _alerts = GetNode<VBoxContainer>("%Alerts");
    _engineeringProgress = GetNode<Label>("%EngineeringProgress");
    _engineeringNotice = GetNode<Label>("%EngineeringNotice");

    GetNode<Button>("%EngineeringButton").Pressed += () => ViewRequested?.Invoke(GeoscapeView.Engineering);
    _speedButton.Pressed += UpdateSpeed;
    _unitsButton.Pressed += () => ViewRequested?.Invoke(GeoscapeView.Units);
    _pauseButton.Toggled += pressed =>
      ChangeSpeed?.Invoke(pressed ? TimeSpeed.Paused : _lastActiveSpeed);
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

  // Rebuilds the alert list — only when membership changes (event fired, expired, or
  // resolved); per-tick countdown updates go through UpdateCountdowns.
  public void RefreshAlerts(SysColGeneric.IReadOnlyList<GeoscapeEvent> activeEvents)
  {
    foreach (Node child in _alerts.GetChildren())
    {
      _alerts.RemoveChild(child); // detach now: replaced alerts must not linger to frame end
      child.QueueFree();
    }
    _alertButtons.Clear();

    foreach (GeoscapeEvent active in activeEvents)
    {
      var button = new Button();
      button.Pressed += () => ResolutionRequested?.Invoke(active);
      _alerts.AddChild(button);
      _alertButtons.Add((button, active));
    }
  }

  // Countdown labels tick down without rebuilding the list.
  public void UpdateCountdowns(long currentTick)
  {
    foreach ((Button button, GeoscapeEvent active) in _alertButtons)
      button.Text = AlertText(active, currentTick);
  }

  private static string AlertText(GeoscapeEvent active, long currentTick)
  {
    string countdown = active.ExpiresAtTick.Match(
      expiresAt => $" ({Math.Max(0L, expiresAt - currentTick) * GeoscapeSession.TickGameSeconds / 60} min)",
      () => "");
    return $"[{active.Definition.Kind}] {active.Definition.Title}{countdown}";
  }
}
