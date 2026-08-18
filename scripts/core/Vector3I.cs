namespace FunProject.Core;

/// <summary>
/// Engine-agnostic 3D integer vector for tile coordinates: X = width, Y = levels/height, Z = depth.
/// The battle runtime uses this so it stays free of Godot types; code at the Godot boundary
/// (authored resources, scene glue) converts explicitly.
/// </summary>
public readonly record struct Vector3I(int X, int Y, int Z)
{
  public static Vector3I Zero { get; } = default;

  public static Vector3I operator +(Vector3I left, Vector3I right) =>
    new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

  public static Vector3I operator -(Vector3I left, Vector3I right) =>
    new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
}
