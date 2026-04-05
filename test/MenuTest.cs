using Godot;
using System;

public partial class MenuTest : Control
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
  {
    var n = GetNode<GridContainer>("HBoxContainer/GridContainer");
    n.FocusMode = FocusModeEnum.All;
    foreach (Node child in n.GetChildren())
		{
			if (child is Button btn)
			{
				btn.GrabFocus();
				break; // Focus the first button and stop
			}
		}
  }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
