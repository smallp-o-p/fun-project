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
    var panel = new VBoxContainer { Name = "Box annotations", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(200, 0) };
    panel.AddChild(_status);
    _inspect = new Button { Text = "Inspect selected box" };
    _inspect.Pressed += InspectSelectedBox;
    panel.AddChild(_inspect);
    _reset = new Button { Text = "Reset annotations…" };
    _reset.Pressed += () =>
    {
      _resetTarget = _grid;
      _confirmReset.PopupCentered();
    };
    panel.AddChild(_reset);
    _confirmReset = new ConfirmationDialog
    {
      Title = "Reset box annotations?",
      DialogText = "Clear all box flags and unlock painting? The painted shape stays.\nThis reset can be undone with Godot's native Undo."
    };
    _confirmReset.Confirmed += ResetAnnotations;
    AddChild(_confirmReset);
    _dock = new EditorDock { Title = "Box annotations", DefaultSlot = EditorDock.DockSlot.RightBl, Global = false, Transient = true };
    _dock.AddChild(panel);
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
    _confirmReset.QueueFree();
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
    if (EditorInterface.Singleton.GetInspector().GetEditedObject() is BattleFootprintCellData inspected &&
        _knownCells.Contains(inspected.GetInstanceId()) && !CanEditCell(inspected))
    {
      // Node history must dispatch native editors when leaving annotation inspection.
      EditorInterface.Singleton.InspectObject(_grid ?? (GodotObject)EditorInterface.Singleton.GetEditedSceneRoot(), inspectorOnly: false);
    }
    if (_grid is null) return;
    var problem = EditingProblem(_grid);
    bool nativeSelection = _nativeGridEditor?.GetCurrentGridMap() == _grid &&
      _grid.TrySelectedCell(_nativeGridEditor.GetSelectedCells(), out _);
    _inspect.Disabled = problem != "" || !nativeSelection;
    _reset.Disabled = EditorInterface.Singleton.GetEditedSceneRoot() != _grid.GetParent() || _grid.Baseline.Count == 0;
    _status.Text = problem != "" ? problem : $"Boxes: {_grid.GetUsedCells().Count} · Cell size {_grid.CellSize} (standalone unit grid)\n" +
      (_grid.Baseline.Count > 0 ? "Layout locked. Select one occupied box to edit flags." : "Paint with Godot's GridMap tools, then select one occupied box.");
    // Close an already-open cell resource as soon as native painting invalidates its coordinates.
    if (problem != "" && EditorInterface.Singleton.GetInspector().GetEditedObject() is BattleFootprintCellData)
      EditorInterface.Singleton.InspectObject(_grid);
  }

  private static string EditingProblem(BattleAnnotationGrid grid)
  {
    if (EditorInterface.Singleton.GetEditedSceneRoot() != grid.GetParent())
      return "Open the reusable BattleProp/BattleFloor scene to annotate; instance overrides are not supported.";
    return grid.AuthoringProblem(Vector3.One);
  }

  private void InspectSelectedBox()
  {
    if (_grid is null || EditingProblem(_grid) != "" || _nativeGridEditor?.GetCurrentGridMap() != _grid ||
        !_grid.TrySelectedCell(_nativeGridEditor.GetSelectedCells(), out var cell)) return;
    if (!_grid.Annotations.Cells.TryGetValue(cell, out var data))
    {
      var next = new BattleFootprintData { Cells = _grid.Annotations.Cells.Duplicate() };
      data = new BattleFootprintCellData { ResourceName = $"Box {cell}" };
      next.Cells[cell] = data;
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

  private bool CanEditCell(BattleFootprintCellData cell) =>
    _grid is not null && EditingProblem(_grid) == "" && _nativeGridEditor?.GetCurrentGridMap() == _grid &&
    _grid.TrySelectedCell(_nativeGridEditor.GetSelectedCells(), out var coordinate) &&
    _grid.Annotations.Cells.TryGetValue(coordinate, out var current) && current == cell;

  private partial class AnnotationInspector(BattleAnnotationEditor editor) : EditorInspectorPlugin
  {
    public override bool _CanHandle(GodotObject obj) => obj is BattleFootprintCellData && editor._knownCells.Contains(obj.GetInstanceId());
    public override void _ParseBegin(GodotObject obj)
    {
      if (!editor.CanEditCell((BattleFootprintCellData)obj))
        AddCustomControl(new Label { Text = "Box editing is locked. Select its occupied cell in the reusable asset; undo layout changes or reset annotations.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }
    public override bool _ParseProperty(GodotObject obj, Variant.Type type, string name, PropertyHint hintType,
      string hintString, PropertyUsageFlags usageFlags, bool wide) => !editor.CanEditCell((BattleFootprintCellData)obj);
  }

  private void ResetAnnotations()
  {
    var grid = _resetTarget;
    _resetTarget = null;
    if (!GodotObject.IsInstanceValid(grid) || EditorInterface.Singleton.GetEditedSceneRoot() != grid!.GetParent()) return;
    var history = GetUndoRedo();
    history.CreateAction("Reset box annotations", customContext: grid);
    history.AddDoProperty(grid, BattleAnnotationGrid.PropertyName.Annotations, new BattleFootprintData());
    history.AddUndoProperty(grid, BattleAnnotationGrid.PropertyName.Annotations, grid.Annotations);
    history.AddDoProperty(grid, BattleAnnotationGrid.PropertyName.Baseline, new Godot.Collections.Dictionary<Godot.Vector3I, Vector2I>());
    history.AddUndoProperty(grid, BattleAnnotationGrid.PropertyName.Baseline, grid.Baseline);
    history.CommitAction();
    EditorInterface.Singleton.InspectObject(grid);
  }
}
#endif
