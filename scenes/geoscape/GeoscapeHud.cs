using FunProject.Engineering;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;

// Authored top bar, left alert list and right project status panel over the permanent map
// view. A full-rect Control (input-ignoring at the root) so hiding the root view hides
// every HUD descendant. Pure presentation: state arrives via pushes; requests flow out as
// C# events and the ViewRequested Godot signal routed by the composition root. Authored
// ordinary Buttons bind their destination PackedScene directly to RequestView. The HUD
// has no per-view branches or button discovery. The completion notice persists until
// replaced by the next completion.
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

  // A real Godot signal: any ordinary Button can bind a PackedScene to this method.
  public void RequestView(PackedScene? scene)
  {
    PackedScene target = scene ?? throw new InvalidOperationException(
      "GeoscapeHud RequestView requires a PackedScene destination.");
    Node instance = target.Instantiate();
    if (instance is not GeoscapeView view)
    {
      string kind = instance.GetClass();
      instance.Free();
      throw new InvalidOperationException(
        $"GeoscapeHud RequestView destination root must be a GeoscapeView; got {kind}.");
    }
    EmitSignal(SignalName.ViewRequested, view);
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
    _alerts.QueueFreeAllChildren();
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
