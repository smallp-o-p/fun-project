#if TOOLS
using Godot;

[Tool]
public partial class BattleAnnotationEditor : EditorPlugin
{
  private EditorDock _dock = null!;
  private bool _dockOpen;
  private Label _status = null!;
  private Button _inspect = null!;
  private Button _reset = null!;
  private ConfirmationDialog _confirmReset = null!;
  private GridMapEditorPlugin? _nativeGridEditor;
  private BattleAnnotationGrid? _grid;
  private BattleAnnotationGrid? _resetTarget;
  private Node? _sceneRoot;
  private readonly SysColGeneric.HashSet<ulong> _knownCells = [];
  private AnnotationInspector _annotationInspector = null!;
  public override void _EnterTree()
  {
    _annotationInspector = new AnnotationInspector(this);
    AddInspectorPlugin(_annotationInspector);
    _dock = GD.Load<PackedScene>("res://addons/battle_authoring/BattleAnnotationDock.tscn").Instantiate<EditorDock>();
    _status = _dock.GetNode<Label>("Panel/Status");
    _inspect = _dock.GetNode<Button>("Panel/Inspect");
    _reset = _dock.GetNode<Button>("Panel/Reset");
    _confirmReset = _dock.GetNode<ConfirmationDialog>("ConfirmReset");
    _inspect.Pressed += InspectSelectedBox;
    _reset.Pressed += () =>
    {
      _resetTarget = _grid;
      _confirmReset.PopupCentered();
    };
    _confirmReset.Confirmed += ResetAnnotations;
    AddDock(_dock);
    _dock.Close();
    SceneChanged += TrackScene;
    TrackScene(EditorInterface.Singleton.GetEditedSceneRoot());
  }
  public override void _ExitTree()
  {
    SceneChanged -= TrackScene;
    if (GodotObject.IsInstanceValid(_sceneRoot)) _sceneRoot!.TreeExiting -= PauseScene;
    RemoveInspectorPlugin(_annotationInspector);
    RemoveDock(_dock);
    _dock.QueueFree();
  }

  // Dependent-scene reload pumps editor frames during teardown. Resume only when
  // selection and Inspector history belong to the newly active scene.
  private void PauseScene() => SetProcess(false);
  private void TrackScene(Node? root)
  {
    if (GodotObject.IsInstanceValid(_sceneRoot)) _sceneRoot!.TreeExiting -= PauseScene;
    _sceneRoot = root;
    if (_sceneRoot is not null) _sceneRoot.TreeExiting += PauseScene;
    SetProcess(true);
  }

  public override bool _Handles(GodotObject obj) => obj is BattleAnnotationGrid;
  public override void _Edit(GodotObject obj)
  {
    // Root deletion/Undo does not emit SceneChanged. Native selection dispatch
    // resumes annotation polling only after a restored grid is back in the tree.
    if (obj is BattleAnnotationGrid grid && GodotObject.IsInstanceValid(grid) && grid.IsInsideTree())
      TrackScene(EditorInterface.Singleton.GetEditedSceneRoot());
  }

  public override void _Process(double delta)
  {
    if (!GodotObject.IsInstanceValid(_nativeGridEditor))
      foreach (var plugin in GetTree().Root.FindChildren("*", "GridMapEditorPlugin", true, false))
        if (plugin is GridMapEditorPlugin native) { _nativeGridEditor = native; break; }
    var nodes = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
    _grid = nodes.Count == 1 ? nodes[0] as BattleAnnotationGrid : null;
    bool showDock = _grid is not null;
    if (showDock != _dockOpen)
    {
      _dockOpen = showDock;
      if (showDock) _dock.Open();
      else _dock.Close();
    }
    if (EditorInterface.Singleton.GetInspector().GetEditedObject() is BattleFootprintData inspected &&
        _knownCells.Contains(inspected.GetInstanceId()) && !CanEditCell(inspected))
    {
      // Node history must dispatch native editors when leaving annotation inspection.
      EditorInterface.Singleton.InspectObject(_grid ?? (GodotObject)EditorInterface.Singleton.GetEditedSceneRoot(), inspectorOnly: false);
    }
    if (_grid is null) return;
    var problem = EditingProblem(_grid);
    bool nativeSelection = _nativeGridEditor?.GetCurrentGridMap() == _grid &&
      _grid.TrySelectedCell(_nativeGridEditor.GetSelectedCells(), out _);
    _inspect.Disabled = problem.IsSome || !nativeSelection;
    _reset.Disabled = EditorInterface.Singleton.GetEditedSceneRoot() != _grid.GetParent() || _grid.Baseline.Count == 0;
    _status.Text = problem.Match(message => message, () => $"Boxes: {_grid.GetUsedCells().Count} · Cell size {_grid.CellSize} (standalone unit grid)\n" +
      (_grid.Baseline.Count > 0 ? "Layout locked. Select one occupied box to edit flags." : "Paint with Godot's GridMap tools, then select one occupied box."));
    // Close an already-open cell resource as soon as native painting invalidates its coordinates.
    if (problem.IsSome && EditorInterface.Singleton.GetInspector().GetEditedObject() is BattleFootprintData)
      EditorInterface.Singleton.InspectObject(_grid);
  }

  private static Option<string> EditingProblem(BattleAnnotationGrid grid)
  {
    if (EditorInterface.Singleton.GetEditedSceneRoot() != grid.GetParent())
      return Some("Open the reusable asset scene to annotate; instance overrides are not supported.");
    return grid.Validate(Vector3.One, requireAnnotations: false);
  }

  private void InspectSelectedBox()
  {
    if (_grid is null || EditingProblem(_grid).IsSome || _nativeGridEditor?.GetCurrentGridMap() != _grid ||
        !_grid.TrySelectedCell(_nativeGridEditor.GetSelectedCells(), out var cell)) return;
    if (!_grid.Annotations.TryGetValue(cell, out var data))
    {
      var next = _grid.Annotations.Duplicate();
      data = new BattleFootprintData { ResourceName = $"Box {cell}" };
      next[cell] = data;
      var history = GetUndoRedo();
      history.CreateAction("Annotate selected box", customContext: _grid);
      history.AddDoProperty(_grid, BattleAnnotationGrid.PropertyName.Annotations, next);
      history.AddUndoProperty(_grid, BattleAnnotationGrid.PropertyName.Annotations, _grid.Annotations);
      if (_grid.Baseline.Count == 0)
      {
        history.AddDoProperty(_grid, BattleAnnotationGrid.PropertyName.Baseline, _grid.CaptureLayout());
        history.AddUndoProperty(_grid, BattleAnnotationGrid.PropertyName.Baseline, _grid.Baseline);
      }
      history.CommitAction();
    }
    _knownCells.Add(data.GetInstanceId());
    EditorInterface.Singleton.InspectObject(data, inspectorOnly: true);
  }

  private bool CanEditCell(BattleFootprintData cell) =>
    _grid is not null && EditingProblem(_grid).IsNone && _nativeGridEditor?.GetCurrentGridMap() == _grid &&
    _grid.TrySelectedCell(_nativeGridEditor.GetSelectedCells(), out var coordinate) &&
    _grid.Annotations.TryGetValue(coordinate, out var current) && current == cell;

  private partial class AnnotationInspector(BattleAnnotationEditor editor) : EditorInspectorPlugin
  {
    public override bool _CanHandle(GodotObject obj) => obj is BattleFootprintData && editor._knownCells.Contains(obj.GetInstanceId());
    public override void _ParseBegin(GodotObject obj)
    {
      if (!editor.CanEditCell((BattleFootprintData)obj))
        AddCustomControl(new Label { Text = "Box editing is locked. Select its occupied cell in the reusable asset; undo layout changes or reset annotations.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }
    public override bool _ParseProperty(GodotObject obj, Variant.Type type, string name, PropertyHint hintType,
      string hintString, PropertyUsageFlags usageFlags, bool wide) => !editor.CanEditCell((BattleFootprintData)obj);
  }

  private void ResetAnnotations()
  {
    var grid = _resetTarget;
    _resetTarget = null;
    if (!GodotObject.IsInstanceValid(grid) || EditorInterface.Singleton.GetEditedSceneRoot() != grid!.GetParent()) return;
    var history = GetUndoRedo();
    history.CreateAction("Reset box annotations", customContext: grid);
    history.AddDoProperty(grid, BattleAnnotationGrid.PropertyName.Annotations, new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>());
    history.AddUndoProperty(grid, BattleAnnotationGrid.PropertyName.Annotations, grid.Annotations);
    history.AddDoProperty(grid, BattleAnnotationGrid.PropertyName.Baseline, new Godot.Collections.Dictionary<Godot.Vector3I, Vector2I>());
    history.AddUndoProperty(grid, BattleAnnotationGrid.PropertyName.Baseline, grid.Baseline);
    history.CommitAction();
    EditorInterface.Singleton.InspectObject(grid);
  }
}
#endif
