using System;
using Godot;

namespace FunProject.Scenes.Ext;

public static class PackedSceneExtensions
{
  /// <summary>
  /// Instantiate once and return the root as <typeparamref name="T"/>; a missing scene or
  /// wrong-typed root is an authoring error naming the <paramref name="role"/> (freed first).
  /// </summary>
  public static T InstantiateAs<T>(this PackedScene? scene, string role) where T : Node
  {
    PackedScene target = scene ?? throw new InvalidOperationException(
      $"{role} requires a PackedScene; assign one in the inspector.");
    Node instance = target.Instantiate();
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
