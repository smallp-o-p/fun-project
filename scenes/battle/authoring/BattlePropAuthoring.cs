using FunProject.Battle;
using Godot;
using System;
using Cell = Godot.Vector3I;

/// <summary>Editor-only metadata root. Export replaces it with plain visual nodes.</summary>
[Tool, GlobalClass]
public partial class BattlePropAuthoring : Node3D
{
  private BattlePropData? _definition;
  private Cell _anchor;
  private int _quarterTurns;
  private MeshInstance3D? _preview;
  private Transform3D _lastTransform;

  [Export]
  public BattlePropData? Definition
  {
    get => _definition;
    set { _definition = value; RefreshIfEditing(); }
  }
  [Export]
  public Cell Anchor
  {
    get => _anchor;
    set { _anchor = value; PlaceIfEditing(); }
  }
  [Export(PropertyHint.Range, "0,3,1")]
  public int QuarterTurns
  {
    get => _quarterTurns;
    set { _quarterTurns = value; PlaceIfEditing(); }
  }
  [ExportToolButton("Snap to grid")]
  private Callable SnapButton => Callable.From(() => TryEditorAction(SnapToGrid));
  [ExportToolButton("Refresh preview")]
  private Callable PreviewButton => Callable.From(RefreshPreview);

  public override void _Ready()
  {
    SetProcess(Engine.IsEditorHint());
    if (Engine.IsEditorHint()) RefreshPreview();
  }
  public override void _Process(double delta)
  {
    if (Transform != _lastTransform) RefreshPreview();
  }
  private void PlaceIfEditing()
  {
    if (IsInsideTree() && Engine.IsEditorHint()) TryEditorAction(ApplyPlacement);
  }
  private void RefreshIfEditing()
  {
    if (IsInsideTree() && Engine.IsEditorHint()) RefreshPreview();
  }
  private void TryEditorAction(Action action)
  {
    try { action(); }
    catch (InvalidOperationException error) { GD.PushError(error.Message); }
    finally { RefreshIfEditing(); }
  }
  private BattleMapAuthoring Map => GetParent() as BattleMapAuthoring
    ?? throw new InvalidOperationException($"{Name}: place the prop directly beneath BattleMapAuthoring.");

  public void ApplyPlacement()
  {
    Map.ValidateGrid();
    if (QuarterTurns is < 0 or > 3)
      throw new InvalidOperationException($"{Name}: Quarter Turns must be 0–3.");
    Transform = new Transform3D(new Basis(Vector3.Up, QuarterTurns * Mathf.Pi / 2), Map.GroundPosition(Anchor));
    RefreshIfEditing();
  }

  public void SnapToGrid()
  {
    var column = Map.LocalToMap(Position);
    Cell? nearest = null;
    float distance = float.PositiveInfinity;
    foreach (var (cell, support) in Map.GetPaintedCells())
    {
      if (cell.X != column.X || cell.Z != column.Z || !support.Walkable) continue;
      float separation = Mathf.Abs(Position.Y - (Map.MapToLocal(cell).Y + support.GroundSurfaceOffset));
      if (separation < distance) { nearest = cell; distance = separation; }
    }
    _anchor = nearest ?? throw new InvalidOperationException($"{Name}: no supporting ground in this column.");
    _quarterTurns = ((Mathf.RoundToInt(Rotation.Y / (Mathf.Pi / 2)) % 4) + 4) % 4;
    ApplyPlacement();
    NotifyPropertyListChanged();
  }

  public BattlePropPlacement GetPlacement()
  {
    if (Definition is null)
      throw new InvalidOperationException($"{Name}: prop definition is missing.");
    if (QuarterTurns is < 0 or > 3)
      throw new InvalidOperationException($"{Name}: Quarter Turns must be 0–3.");
    var expected = new Transform3D(new Basis(Vector3.Up, QuarterTurns * Mathf.Pi / 2), Map.GroundPosition(Anchor));
    if (!Transform.IsEqualApprox(expected))
      throw new InvalidOperationException($"{Name}: transform does not match anchor/rotation; use Snap to grid. Scale and tilt are unsupported.");
    return new(Definition, Anchor, QuarterTurns);
  }

  public Node3D CopyVisual()
  {
    var visual = new Node3D { Name = Name, Transform = Transform };
    foreach (var child in GetChildren())
      if (child != _preview) visual.AddChild(child.Duplicate());
    return visual;
  }

  public void RefreshPreview()
  {
    _lastTransform = Transform;
    if (_preview is not null && GodotObject.IsInstanceValid(_preview)) _preview.Free();
    _preview = null;
    if (Definition?.Footprint is null || Definition.Footprint.Count == 0)
    {
      UpdateConfigurationWarnings();
      return;
    }
    bool valid = true;
    try
    {
      Map.ValidateGrid();
      var placements = new SysColGeneric.List<BattlePropPlacement>();
      foreach (var child in Map.GetChildren())
        if (child is BattlePropAuthoring prop) placements.Add(prop.GetPlacement());
      BattleMapBaker.Bake(Map.GetPaintedCells(), placements);
    }
    catch (InvalidOperationException) { valid = false; }
    var mesh = new ImmediateMesh();
    var material = new StandardMaterial3D
    {
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
      AlbedoColor = valid ? Colors.LimeGreen : Colors.OrangeRed,
      NoDepthTest = true
    };
    mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
    foreach (var cell in Definition.Footprint)
    {
      var c = new Vector3(cell.X, 0.025f, cell.Z);
      Vector3[] corners = [c + new Vector3(-0.5f, 0, -0.5f), c + new Vector3(0.5f, 0, -0.5f), c + new Vector3(0.5f, 0, 0.5f), c + new Vector3(-0.5f, 0, 0.5f)];
      for (int i = 0; i < 4; i++) Line(mesh, corners[i], corners[(i + 1) % 4]);
    }
    foreach (var edge in Definition.CoverEdges ?? [])
    {
      if (edge is null || edge.Amount <= 0) continue;
      Vector3 direction = edge.Direction switch
      {
        CoverDirections.North => Vector3.Forward,
        CoverDirections.South => Vector3.Back,
        CoverDirections.East => Vector3.Right,
        CoverDirections.West => Vector3.Left,
        _ => Vector3.Zero
      };
      var start = new Vector3(edge.Cell.X, 0.04f, edge.Cell.Z) + direction * 0.5f;
      var end = start + direction * 0.35f;
      var side = direction.Cross(Vector3.Up) * 0.12f;
      Line(mesh, start, end);
      Line(mesh, end, end - direction * 0.12f + side);
      Line(mesh, end, end - direction * 0.12f - side);
    }
    mesh.SurfaceEnd();
    _preview = new MeshInstance3D { Name = "FootprintPreview", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    AddChild(_preview, false, InternalMode.Back);
    UpdateConfigurationWarnings();
  }
  private static void Line(ImmediateMesh mesh, Vector3 from, Vector3 to)
  {
    mesh.SurfaceAddVertex(from); mesh.SurfaceAddVertex(to);
  }
  public override string[] _GetConfigurationWarnings()
  {
    try { GetPlacement(); return []; }
    catch (InvalidOperationException error) { return [error.Message]; }
  }
}
