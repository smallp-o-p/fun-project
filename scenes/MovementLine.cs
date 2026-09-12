using Godot;
using System.Collections.Generic;

[Tool]
public partial class MovementLine : MeshInstance3D
{
  private Vector3[] _points = [];
  private Color _lineColor = new(0.25f, 0.75f, 1.0f, 1.0f);
  private float _cornerRadius = 0.25f;
  private float _width = 0.06f;
  private ShaderMaterial? _shader;

  public MovementLine()
  {
    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
  }

  [Export]
  public Vector3[] Points { get => _points; set { _points = value; if (Engine.IsEditorHint()) Rebuild(); } }

  [Export]
  public Color LineColor { get => _lineColor; set { _lineColor = value; if (Engine.IsEditorHint()) Rebuild(); } }

  [Export]
  public float CornerRadius { get => _cornerRadius; set { _cornerRadius = value; if (Engine.IsEditorHint()) Rebuild(); } }

  [Export]
  public float Width { get => _width; set { _width = value; if (Engine.IsEditorHint()) Rebuild(); } }

  [Export]
  public ShaderMaterial? Shader { get => _shader; set { _shader = value; if (Engine.IsEditorHint()) Rebuild(); } }

  public override void _Ready()
  {
    EnsureMaterials();
    if (Engine.IsEditorHint())
      Rebuild();
    else
      Visible = false;
  }

  public void ShowPath(IReadOnlyList<Vector3> points)
  {
    Points = [.. points];
    Rebuild();
  }

  public void Rebuild()
  {
    EnsureMaterials();
    Mesh = MovementLineBuilder.CreateMesh(Points, CornerRadius, Width);
    ((StandardMaterial3D)MaterialOverride).AlbedoColor = LineColor;
    Visible = Mesh.GetSurfaceCount() > 0;
  }

  public override void _Notification(int what)
  {
    if (!Engine.IsEditorHint())
      return;

    switch (what)
    {
      case (int)Node.NotificationEditorPreSave:
        Mesh = null;
        break;
      case (int)Node.NotificationEditorPostSave:
        Rebuild();
        break;
    }
  }

  private void EnsureMaterials()
  {
    if (MaterialOverride is not StandardMaterial3D)
    {
      MaterialOverride = new StandardMaterial3D
      {
        AlbedoColor = LineColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled
      };
    }

    if (Shader != null)
      MaterialOverlay = Shader;
  }
}
