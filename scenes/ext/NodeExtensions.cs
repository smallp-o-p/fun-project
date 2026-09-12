namespace FunProject.Scenes.Ext;
using Godot;

public static class NodeExtensions
{
  extension(Node node)
  {
    public void QueueFreeAllChildren()
    {
      foreach(Node child in node.GetChildren()){
        node.RemoveChild(child);
        child.QueueFree();
      }
    }
  }
}