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
  private Label _engineeringProgress = null!;
  private Label _engineeringRemaining = null!;

  private TimeSpeed _lastActiveSpeed = TimeSpeed.Normal;

  public event Action<TimeSpeed>? ChangeSpeed;

  [Signal] public delegate void ViewRequestedEventHandler(GeoscapeView view);

  public override void _Ready()
  {
    _speedButton = GetNode<Button>("%SpeedButton");
    _pauseButton = GetNode<Button>("%PauseButton");
    _clockLabel = GetNode<Label>("%ClockLabel");
    _engineeringProgress = GetNode<Label>("%EngineeringProgress");
    _engineeringRemaining = GetNode<Label>("%EngineeringRemaining");

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
      job => job.Project.Item.Name,
      () => "No active manufacturing.");
    _engineeringProgress.TooltipText = manufacturing.Match(job => job.Project.Item.Name, () => "");
    _engineeringRemaining.Text = manufacturing.Match(
      job => RemainingDays(job.CompletesAtTick, tick), () => "");
    _engineeringRemaining.Visible = manufacturing.IsSome;
  }

  private static string RemainingDays(long completesAtTick, long tick)
  {
    long ticks = completesAtTick <= tick ? 0 : completesAtTick - tick;
    long ticksPerDay = (TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond) / GeoscapeSession.TickGameSeconds;
    // A partial day still needs work: keep 1d visible until the job actually completes.
    long days = ticks / ticksPerDay + (ticks % ticksPerDay == 0 ? 0 : 1);
    return $"{days}d remaining";
  }

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
}
