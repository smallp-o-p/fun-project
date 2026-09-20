using System;
using Godot;

namespace FunProject.Scenes.Ext;

public static class PackedSceneExtensions
{
  public static T InstantiateAs<T>(this PackedScene scene, string role = "") where T : Node
  {
    Node instance = scene.Instantiate();

    if (instance is not T typed)
    {
      string kind = instance.GetClass();
      instance.Free();
      throw new InvalidOperationException(
        $"{role} root must be a {typeof(T).Name}; got {kind}.");
    }

    return typed;
  }
}
