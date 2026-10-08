using Godot;

namespace FunProject.Scenes;

[Tool, GlobalClass]
public partial class BattleAnnotationMarker : MeshInstance3D
{
  public const string FootprintMeta = "footprint_data";
  private BattleFootprintData _defaults = new();
  private Mesh? _annotatedMesh;

  [Export]
  public BattleFootprintData Defaults
  {
    get => _defaults;
    set { _defaults = value; StoreDefaults(); }
  }

  public override void _Ready() => StoreDefaults();
  public override void _Process(double delta)
  {
    if (Engine.IsEditorHint() && Mesh != _annotatedMesh) StoreDefaults();
  }
  public override void _Notification(int what)
  {
    if (what == NotificationEditorPreSave) StoreDefaults();
  }

  private void StoreDefaults()
  {
    if (Mesh is null) return;
    // Keep the same resource so Inspector edits to its fields also update metadata.
    Mesh.SetMeta(FootprintMeta, _defaults);
    _annotatedMesh = Mesh;
  }
}
