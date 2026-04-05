using Godot;
using System;

public partial class Main : Control
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
  {
    var enemies = GetNode<EnemySpriteContainer>("Control/MainBattle/EnemySpriteContainer");

    enemies.NumRows = 4;
    enemies.NumCols = 4;
    enemies.Stagger = 10;

    enemies._Ready();

    for(int i = 0; i < 4; i++)
    {
      for(int j = 0; j < 4; j++ )
      {
        TextureRect textRect = new TextureRect();
        textRect.Texture = GD.Load<Texture2D>("res://Circle.tres");
        enemies.AddEntity(textRect, i, j);
      }
    }

  }

  private double time = 0.0;

  private bool replaced = false;
  public override void _Process(double delta)
  {
    base._Process(delta);

    if(!replaced && time >= 10.0)
    {
      var enemies = GetNode<EnemySpriteContainer>("Control/MainBattle/EnemySpriteContainer");
      TextureRect textRect2 = new TextureRect();
      textRect2.Texture = GD.Load<Texture2D>("res://icon.svg");
      enemies.AddEntity(textRect2, 0, 0);
      replaced = true;
    }

    time += delta;
  }

}
