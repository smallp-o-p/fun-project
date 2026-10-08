using Godot;
using System;

namespace FunProject;

public partial class Main : Control
{
  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
  }

  private double time;

  private bool replaced;
  public override void _Process(double delta)
  {
    base._Process(delta);
  }

}
