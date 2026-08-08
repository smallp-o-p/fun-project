using Godot;

[Tool]
public partial class MovementLine : MeshInstance3D
{
  [Export]
  public Vector3[] Points { get; set; } =
  [
    new(0, 0, 0),
    new(1, 0, 0),
    new(1, 0, 1)
  ];

  [Export] public Color LineColor { get; set; } = new(0.25f, 0.75f, 1.0f, 1.0f);
  [Export] public float CornerRadius { get; set; } = 0.25f;
  [Export] public int CornerSegments { get; set; } = 16;
  [Export] public float Width { get; set; } = 0.06f;
  [Export] public ShaderMaterial Shader { get; set; }

  public override void _Ready()
  {
    Rebuild();
  }

  public void Rebuild()
  {
    MeshInstance3D preview = MovementLineBuilder.CreateLine(
      Points,
      LineColor,
      CornerRadius,
      CornerSegments,
      Width);

    Mesh = preview.Mesh;
    MaterialOverlay = Shader;

    Visible = preview.Visible;
  }
}
