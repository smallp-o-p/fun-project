using Godot;

namespace FunProject.Models;

/// <summary>A fixed imported garment path; variant -1 belongs to every outfit.</summary>
[Tool]
public partial class ModelWardrobeGarment : Resource
{
  [Export] private NodePath _path = new();
  [Export] private int _variant = -1;

  public NodePath Path => _path;
  public int Variant => _variant;

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (((PropertyUsageFlags)property["usage"].AsInt64()).HasFlag(PropertyUsageFlags.ScriptVariable))
      property["usage"] = (long)PropertyUsageFlags.Storage;
  }
}
