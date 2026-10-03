using Godot;
using System;

// Editable entity source: native annotation painting and model geometry export together.
[Tool, GlobalClass]
public partial class BattleEntityAnnotator : Node3D
{
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Annotations { get; set; } = [];
  private MeshLibrary? _markers;
  [Export]
  public MeshLibrary? Markers
  {
    get => _markers;
    set
    {
      _markers = value;
      if (GetNodeOrNull<GridMap>("Annotations") is { } grid) grid.MeshLibrary = value;
    }
  }
  [Export(PropertyHint.File, "*.tscn,*.scn")] public string ExportPath { get; set; } = "res://resources/map_entities/example_entity.tscn";

  public GridMap Grid => GetNode<GridMap>("Annotations");

  [ExportToolButton("Export BattleProp")] private Callable ExportButton => Callable.From(Export);

  public override void _Ready() => Grid.MeshLibrary = Markers;

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsStringName() == PropertyName.Annotations)
      property["usage"] = (int)PropertyUsageFlags.Storage;
  }

  public bool TrySelectedCell(Godot.Collections.Array selected, out Godot.Vector3I cell)
  {
    cell = selected.Count == 1 ? selected[0].AsVector3I() : default;
    return selected.Count == 1 && MarkerDefaults(Grid.GetCellItem(cell)).IsSome;
  }

  private Option<BattleFootprintData> MarkerDefaults(int item)
  {
    if (Markers is null || System.Array.IndexOf(Markers.GetItemList(), item) < 0) return None;
    var mesh = Markers.GetItemMesh(item);
    return mesh is not null && mesh.HasMeta(BattleAnnotationMarker.FootprintMeta) &&
      mesh.GetMeta(BattleAnnotationMarker.FootprintMeta).AsGodotObject() is BattleFootprintData data ? data : None;
  }

  internal BattleFootprintData CreateDefaultAnnotation(Godot.Vector3I cell) => MarkerDefaults(Grid.GetCellItem(cell)).Match(
    defaults => (BattleFootprintData)defaults.Duplicate(),
    () => throw new InvalidOperationException($"Cell {cell} needs a brush mesh with footprint metadata."));

  public Option<string> Validate()
  {
    if (GetNodeOrNull("Annotations") is not GridMap)
      return "Add a plain GridMap child named Annotations to this entity source.";
    if (GetNodeOrNull("Model") is not MeshInstance3D { Mesh: not null } model || model.GetChildCount() != 0)
      return "Use one MeshInstance3D named Model with a mesh and no child models.";
    if (Markers is null) return "Assign a brush MeshLibrary before painting.";
    if (!Grid.Transform.IsEqualApprox(Transform3D.Identity) || Grid.TopLevel || !Grid.CellCenterX || !Grid.CellCenterY || !Grid.CellCenterZ || !Mathf.IsEqualApprox(Grid.CellScale, 1))
      return "Annotation boxes need an identity transform, centered cells and Cell Scale 1.";
    if (!Grid.CellSize.IsEqualApprox(Vector3.One)) return "Annotation boxes require unit Cell Size.";

    foreach (var cell in Grid.GetUsedCells())
    {
      if (MarkerDefaults(Grid.GetCellItem(cell)).IsNone) return $"Cell {cell} needs a brush mesh with footprint metadata.";
      if (!BattleMapAuthoring.IsUpright(Grid.GetCellItemBasis(cell)))
        return $"Box {cell} has pitch or roll. Only upright Y quarter-turns are supported.";
    }
    return None;
  }

  public Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> BuildFootprint()
  {
    Validate().IfSome(problem => throw new InvalidOperationException($"{Name}: {problem}"));
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> result = [];
    foreach (var cell in Grid.GetUsedCells())
      result[cell] = Annotations.TryGetValue(cell, out var data) ? (BattleFootprintData)data.Duplicate() : CreateDefaultAnnotation(cell);
    return result;
  }

  public PackedScene BuildScene()
  {
    var footprint = BuildFootprint();
    var model = GetNode<MeshInstance3D>("Model");
    var mesh = (Mesh)model.Mesh.Duplicate();
    for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
      mesh.SurfaceSetMaterial(surface, model.GetActiveMaterial(surface));
    mesh.SetMeta(BattleProp.FootprintMeta, footprint);
    var prop = new BattleProp { Name = Name, Mesh = mesh, Transform = model.Transform };
    try
    {
      // Native MeshLibrary export carries these floor-picking shapes with the mesh.
      var floor = new StaticBody3D { Name = "FloorSurface", Transform = model.Transform.AffineInverse(), CollisionMask = 0 };
      foreach (var (cell, flags) in footprint)
      {
        if (!flags.HasFloor) continue;
        floor.AddChild(new CollisionShape3D
        {
          Position = (Vector3)cell + new Vector3(0.5f, -0.01f, 0.5f),
          Shape = new BoxShape3D { Size = new(1, 0.02f, 1) }
        });
      }
      if (floor.GetChildCount() == 0) floor.Free();
      else
      {
        prop.AddChild(floor);
        floor.Owner = prop;
        foreach (var shape in floor.GetChildren()) shape.Owner = prop;
      }
      var scene = new PackedScene();
      var error = scene.Pack(prop);
      if (error == Error.Ok) return scene;
      scene.Dispose();
      throw new InvalidOperationException($"Could not pack entity {Name}: {error}.");
    }
    finally { prop.Free(); }
  }

  public void Export()
  {
    if ((!ExportPath.StartsWith("res://", StringComparison.Ordinal) && !ExportPath.StartsWith("user://", StringComparison.Ordinal)) ||
        (!ExportPath.EndsWith(".tscn", StringComparison.Ordinal) && !ExportPath.EndsWith(".scn", StringComparison.Ordinal)))
      throw new InvalidOperationException("Choose a separate .tscn or .scn Export Path.");
    if (!string.IsNullOrEmpty(SceneFilePath) && string.Equals(
      System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(ExportPath)),
      System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(SceneFilePath)), StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("Export to a separate scene; the editable source cannot be overwritten.");
    using var scene = BuildScene();

    var error = ResourceSaver.Save(scene, ExportPath);
    if (error != Error.Ok) throw new InvalidOperationException($"Could not export entity to {ExportPath}: {error}.");

    scene.TakeOverPath(ExportPath);
    _ = ResourceLoader.Load<PackedScene>(ExportPath, cacheMode: ResourceLoader.CacheMode.Replace);
#if TOOLS
    if (Engine.IsEditorHint() && IsInsideTree()) EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(ExportPath);
#endif
  }

}
