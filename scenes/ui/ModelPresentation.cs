using Godot;
using System;

/// <summary>
/// A presentation-only model in an isolated, transparent 3D world. Callers choose the
/// scene explicitly; campaign combatants currently have no authored model association.
/// Keeps the model's appearance setup and never forwards UI input into the model.
/// </summary>
public sealed partial class ModelPresentation : SubViewportContainer
{
  [Export] public PackedScene? ModelScene { get; set; }
  [Export(PropertyHint.Range, "0.1,2,0.01")] public float FramingHeight { get; set; } = 0.8f;
  [Export(PropertyHint.Range, "0,1,0.01")] public float FocusHeight { get; set; } = 0.62f;
  [Export] public float ModelYawDegrees { get; set; }
  [Export] public bool FitWidth { get; set; } = true;

  private PackedScene? _presentedScene;
  private Node3D? _model;

  public override void _Ready()
  {
    Resized += FrameModel;
    PresentModel(ModelScene);
  }

  public void PresentModel(PackedScene? scene)
  {
    ModelScene = scene;
    if (!IsNodeReady() || ReferenceEquals(scene, _presentedScene))
      return;

    _model?.Free();
    _model = null;
    _presentedScene = scene;
    if (scene is null)
      return;

    Node instance = scene.Instantiate();
    if (instance is not Node3D model)
    {
      instance.Free();
      _presentedScene = null;
      throw new InvalidOperationException("A model presentation requires a Node3D scene root.");
    }

    _model = model;
    var root = GetNode<Node3D>("%ModelRoot");
    root.RotationDegrees = new Vector3(0, ModelYawDegrees, 0);
    root.AddChild(model); // authored visual masks, wardrobe and pose initialize normally
    DisableInput(model);
    FrameModel();
  }

  private static void DisableInput(Node node)
  {
    node.SetProcessInput(false);
    node.SetProcessUnhandledInput(false);
    node.SetProcessUnhandledKeyInput(false);
    node.SetProcessShortcutInput(false);
    foreach (Node child in node.GetChildren())
      DisableInput(child);
  }

  private void FrameModel()
  {
    if (_model is null || !IsInsideTree())
      return;

    Aabb? bounds = null;
    CollectBounds(_model, ref bounds);
    if (bounds is not Aabb box)
      return;

    float height = Mathf.Max(box.Size.Y, 0.1f);
    float aspect = Mathf.Max(Size.X, 1) / Mathf.Max(Size.Y, 1);
    var camera = GetNode<Camera3D>("%ModelCamera");
    camera.Size = FitWidth
      ? Mathf.Max(height * FramingHeight, box.Size.X * 1.08f / aspect)
      : height * FramingHeight;
    camera.Position = new Vector3(box.GetCenter().X,
      box.Position.Y + height * FocusHeight,
      box.End.Z + Mathf.Max(height * 2, 1));
    camera.Near = 0.01f;
    camera.Far = Mathf.Max(height * 5 + box.Size.Z, 10);
  }

  private static void CollectBounds(Node node, ref Aabb? bounds)
  {
    if (node is MeshInstance3D mesh && mesh.Mesh is not null && mesh.IsVisibleInTree())
    {
      Aabb transformed = mesh.GlobalTransform * mesh.GetAabb();
      bounds = bounds is Aabb current ? current.Merge(transformed) : transformed;
    }
    foreach (Node child in node.GetChildren())
      CollectBounds(child, ref bounds);
  }
}
