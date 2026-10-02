using FunProject.Dialogue;
using Godot;
using System;

// Click-through dialogue screen: a transparent GeoscapeView whose dim wash covers the
// active stack while the conversation plays. The view is in control while it is the
// stack top: clicks on the dim wash and ui_accept both advance (skip-to-full first,
// then next line), and the final advance emits Finished and pops the view through
// RequestBack. Hosts without a view manager consume Finished and free the view
// themselves; that handler must QueueFree the view, never Free it, because the
// immediately following RequestBack emits a signal on the freed view. Playback
// starts in _Ready from the sequence given to Configure before
// tree entry; authoring bugs (empty lines/text/speaker/portrait) throw in _Ready.
public sealed partial class DialogueView : GeoscapeView
{
  [Export] public double CharactersPerSecond { get; set; } = 40;

  public event Action? Finished;

  private DialogueSequenceData _sequence = null!;
  private SpeakerData? _currentSpeaker;
  private Node? _portraitModel;
  private int _lineIndex;
  private double _revealSeconds;
  private double _blinkSeconds;
  private bool _revealed;
  private Label _speakerName = null!;
  private Label _body = null!;
  private Label _continue = null!;
  private Node3D _modelRoot = null!;
  private ScrollContainer _bodyScroll = null!;

  private DialogueLineData CurrentLine => _sequence.Lines[_lineIndex];

  public void Configure(DialogueSequenceData sequence)
  {
    ArgumentNullException.ThrowIfNull(sequence);
    if (_sequence is not null)
      throw new InvalidOperationException(
        "DialogueView is already configured; push a fresh view per dialogue.");
    _sequence = sequence;
  }

  public override void _Ready()
  {
    base._Ready();
    if (_sequence is null)
      throw new InvalidOperationException(
        "DialogueView requires Configure(sequence) before it enters the tree.");
    _sequence.EnsurePlayable();

    _speakerName = GetNode<Label>("%SpeakerName");
    _body = GetNode<Label>("%Body");
    _continue = GetNode<Label>("%Continue");
    _modelRoot = GetNode<Node3D>("%ModelRoot");
    _bodyScroll = GetNode<ScrollContainer>("%BodyScroll");
    _bodyScroll.GuiInput += OnDimGuiInput;
    GetNode<Control>("%Dim").GuiInput += OnDimGuiInput;

    ShowLine();
  }

  public override void _PhysicsProcess(double delta)
  {
    TickReveal(delta);
    TickBlink(delta);
  }

  public override void _Input(InputEvent @event)
  {
    if (@event.IsActionPressed("ui_accept"))
    {
      GetViewport().SetInputAsHandled();
      Advance();
    }
  }

  internal void OnDimGuiInput(InputEvent @event)
  {
    if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
      Advance();
  }

  internal void TickReveal(double delta)
  {
    if (_revealed)
      return;
    _revealSeconds += delta;
    RenderReveal();
  }

  internal void Advance()
  {
    // Unreachable in the managed pop flow; protects future QueueFree-on-Finished hosts.
    if (_lineIndex >= _sequence.Lines.Length)
      return;
    if (!_revealed)
    {
      _body.VisibleCharacters = -1;
      _revealed = true;
      _continue.Show();
      return;
    }

    _lineIndex++;
    if (_lineIndex >= _sequence.Lines.Length)
    {
      Finished?.Invoke();
      RequestBack();
      return;
    }
    ShowLine();
  }

  private void ShowLine()
  {
    DialogueLineData line = CurrentLine;
    _speakerName.Text = line.Speaker!.DisplayName;
    _body.Text = line.Text;
    _bodyScroll.ScrollVertical = 0;
    _revealSeconds = 0;
    _blinkSeconds = 0;
    _continue.Modulate = Colors.White;
    _revealed = false;
    _continue.Hide();
    SetPortrait(line.Speaker!);
    RenderReveal();
  }

  private void SetPortrait(SpeakerData speaker)
  {
    // Authored .tres loads share one instance, so reference equality keeps the portrait
    // model across consecutive lines by the same speaker.
    if (_currentSpeaker == speaker)
      return;
    _currentSpeaker = speaker;
    _portraitModel?.Free(); // free now: the swapped model must not linger to frame end
    _portraitModel = speaker.Portrait!.Instantiate();
    _modelRoot.AddChild(_portraitModel);
  }

  private void RenderReveal()
  {
    _body.VisibleCharacters = (int)(_revealSeconds * CharactersPerSecond);
    if (_body.VisibleCharacters >= _body.GetTotalCharacterCount())
    {
      _revealed = true;
      _continue.Show();
    }
  }

  private void TickBlink(double delta)
  {
    if (!_continue.Visible)
      return;
    _blinkSeconds += delta;
    _continue.Modulate = new Color(1, 1, 1, Math.Floor(_blinkSeconds * 2) % 2 == 0 ? 1f : 0.25f);
  }
}
