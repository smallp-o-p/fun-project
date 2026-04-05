using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;

public partial class ActionDialogueBox : VBoxContainer
{
  private class TimedLine
  {
    public Label Label { get; set; }
    public double CreationTime { get; set; }
    public Tween FadeTween { get; set; }
    public ulong LifetimeSeconds { get; set; }
    public void Dispose()
    {
      Label.Free();
      FadeTween.Dispose();
    }
  }

  private Queue<TimedLine> lines = new Queue<TimedLine>();
  private Queue<TimedLine> display = new Queue<TimedLine>();
  private double currentTime = 0.0d;

  [Export] public float FadeTime { get; set; } = 1.0f;
  [Export] public int MaxLinesAtOnce = 10;

  public override void _Ready()
  {
    base._Ready();
    base.Alignment = AlignmentMode.End;
  }

  public override void _Process(double delta)
  {
    currentTime += delta;

    if (lines.Count == 0)
    {
      return;
    }

    while(display.Count < MaxLinesAtOnce && lines.Count > 0)
    {
      TimedLine l = lines.Dequeue();

      l.CreationTime = currentTime;

      AddToDisplay(l);
    }

    if(display.Count == 0 || display.Count != MaxLinesAtOnce)
    {
      return;
    }

    // Fade only the first line, let others linger until it's their turn
    var line = display.Peek();
    double age = currentTime - line.CreationTime;
    double timeToStartFade = line.LifetimeSeconds - FadeTime;

    if (line.FadeTween == null && age > timeToStartFade)
    {
      StartFade(line);
    }
    else if (age >= line.LifetimeSeconds)
    {
      PopDisplay();
    }

  }

  private void AddToDisplay(TimedLine line)
  {
    display.Enqueue(line);
    AddChild(line.Label);
  }

  private void PopDisplay()
  {
    TimedLine l = display.Dequeue();
    RemoveChild(l.Label);
    l.Dispose();
  }

  public void QueueLine(Label label, ulong lifetimeSeconds)
  {
    if(lifetimeSeconds <= FadeTime)
    {
      throw new ArgumentException("Lifetime Seconds Should be >= FadeTime");
    }

    lines.Enqueue(new TimedLine { Label = label, FadeTween = null, LifetimeSeconds = lifetimeSeconds });
  }

  private void StartFade(TimedLine line)
  {
    Tween FadeTween = CreateTween();
    FadeTween.TweenProperty(line.Label, "modulate:a", 0.0f, FadeTime);
    line.FadeTween = FadeTween;
  }

  public void ClearAll()
  {
    foreach (TimedLine l in lines)
    {
      l.Dispose();
    }
    lines.Clear();
  }
}
