#if TOOLS
using Godot;
using System.Collections.Generic;

[Tool]
public partial class FootprintEditor : EditorPlugin
{
  private readonly FootprintGizmo _gizmo = new();
  public override void _EnterTree()
  {
    AddNode3DGizmoPlugin(_gizmo);
    EditorInterface.Singleton.GetInspector().PropertyEdited += Refresh;
  }
  public override void _ExitTree()
  {
    EditorInterface.Singleton.GetInspector().PropertyEdited -= Refresh;
    RemoveNode3DGizmoPlugin(_gizmo);
  }
  private void Refresh(string property) => RefreshTree(EditorInterface.Singleton.GetEditedSceneRoot());
  private static void RefreshTree(Node? node)
  {
    if (node is null) return;
    if (node is BattleFloor or BattleProp) ((Node3D)node).UpdateGizmos();
    foreach (var child in node.GetChildren()) RefreshTree(child);
  }

  private partial class FootprintGizmo : EditorNode3DGizmoPlugin
  {
    public FootprintGizmo() => CreateMaterial("cells", new Color(0.2f, 0.9f, 0.8f));
    public override bool _HasGizmo(Node3D node) => node is BattleProp or BattleFloor;
    public override string _GetGizmoName() => "Battle footprint";
    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
      gizmo.Clear();
      var node = gizmo.GetNode3D();
      var footprint = node is BattleProp prop ? prop.Footprint : ((BattleFloor)node).Footprint;
      if (footprint is null) return;
      var size = Vector3.One;
      for (Node parent = node; parent is not null; parent = parent.GetParent())
        if (parent is BattleMapAuthoring map) { size = new(map.CellWidth, map.LevelHeight, map.CellWidth); break; }
      var lines = new List<Vector3>();
      foreach (var cell in footprint.Cells.Keys)
        for (int corner = 0; corner < 8; corner++)
        {
          var point = ((Vector3)cell + new Vector3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1)) * size;
          for (int axis = 0; axis < 3; axis++)
          {
            if ((corner & (1 << axis)) != 0) continue;
            var end = point;
            end[axis] += size[axis];
            lines.Add(point); lines.Add(end);
          }
        }
      gizmo.AddLines(lines.ToArray(), GetMaterial("cells", gizmo));
    }
  }
}
#endif
