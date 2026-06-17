using Godot;
using System;

public partial class Main : Control
{
  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
  }

  private double time = 0.0;

  private bool replaced = false;
  public override void _Process(double delta)
  {
    base._Process(delta);
  }

}
