using Godot;

namespace FunProject.Models;

/// <summary>Import-validated mask contribution; its bound region remains per-instance.</summary>
[Tool]
public partial class ModelWardrobeMaskRule : Resource
{
  [Export] private NodePath _path = new();
  [Export] private StringName _name = new();
  [Export] private int _index;
  [Export] private string _component = "";
  [Export] private bool _enabled = true;
  [Export] private int _variant = -1;

  public NodePath Path => _path;
  public StringName Name => _name;
  public int Index => _index;
  public string Component => _component;
  public bool Enabled => _enabled;
  public int Variant => _variant;

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (((PropertyUsageFlags)property["usage"].AsInt64()).HasFlag(PropertyUsageFlags.ScriptVariable))
      property["usage"] = (long)PropertyUsageFlags.Storage;
  }
}
