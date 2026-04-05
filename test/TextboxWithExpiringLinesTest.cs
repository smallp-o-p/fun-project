using System;
using System.Diagnostics.CodeAnalysis;
using Godot;

public partial class TextboxWithExpiringLinesTest : Node
{
  ActionDialogueBox textBox;
  int counter = 1;

  ulong timer = Time.GetTicksMsec();
  public override void _Ready()
  {
    textBox = GetNode<ActionDialogueBox>("MarginContainer/DialogueBox");
    textBox.FadeTime = 0.75f;
    textBox.MaxLinesAtOnce = 5;
  }
  public override void _Process(double delta)
  {
    var now = Time.GetTicksMsec();
    if ((now - timer) >= 750)
    {
      Label label = new() { Text = string.Format("Line {0}", counter++) };
      textBox.QueueLine(label, 2);
      timer = now;
    }
  }
}
