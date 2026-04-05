using Godot;

public partial class EnemySpriteContainer : VBoxContainer
{

  private struct Row
  {
    public MarginContainer wrapper;
    public HBoxContainer actualContents;
  }

  [Export] public int NumRows { set; get; } = 3;
  [Export] public int NumCols { set; get; } = 3;
  [Export] public int Stagger { set; get; }
  // Placeholder texture for if there's nothing on a particular square.
  [Export] public Texture2D EmptyTexture { get; set; }

  private Row[] rows;
  private int currRow = 0;

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    Alignment = AlignmentMode.Center;
    rows = new Row[NumRows];

    for (int i = 0; i < NumRows; i++)
    {
      rows[i].wrapper = new();
      rows[i].wrapper.AddThemeConstantOverride("margin_left", i * Stagger);
      rows[i].wrapper.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

      rows[i].actualContents = new();

      rows[i].wrapper.AddChild(rows[i].actualContents);

      for(int j = 0; j < NumCols; j++)
      {
        TextureRect rect = new()
        {
          Texture = EmptyTexture
        };
        rows[i].actualContents.AddChild(rect);
      }

      AddChild(rows[i].wrapper);
    }
  }

  // Replaces element at [x,y] if one exists.
  public void AddEntity(Node n, int x, int y)
  {
    HBoxContainer box = rows[x].actualContents;
    Node oldChild = box.GetChild(y);

    if(oldChild != null)
    {
      oldChild.ReplaceBy(n);
      oldChild.QueueFree();
    }
  }

  public void SetStagger(int n)
  {
    Stagger = n;
    for(int i = 0; i < NumRows; i++)
    {
      rows[i].wrapper.AddThemeConstantOverride("margin_left", i * Stagger);
    }
  }
}
