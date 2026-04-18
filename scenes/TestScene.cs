using Godot;
using System;

public partial class TestScene : Node3D
{
  public Vector3[] positionsOnCircle { get; set; }
  public int currentPos = 0;

  public override void _Ready()
  {
  }

  public override void _Process(double delta)
  {
    base._Process(delta);
  }

}
