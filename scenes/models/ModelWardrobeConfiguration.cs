using System;
using System.Collections.ObjectModel;
using Godot;

namespace FunProject.Models;

/// <summary>Import-generated shared catalog. Blender owns definitions; instances own selections.</summary>
[Tool]
public partial class ModelWardrobeConfiguration : Resource
{
  // Storage-only fields are populated by the importer/ResourceLoader, never the Inspector.
  [Export] private string[] _variants = [];
  [Export] private int _defaultVariant;
  [Export] private Godot.Collections.Dictionary<string, ModelWardrobeComponent> _components = new();
  [Export] private Godot.Collections.Array<ModelWardrobeMaskRule> _masks = [];

  public SysColGeneric.IReadOnlyList<string> Variants => System.Array.AsReadOnly(_variants);
  public int DefaultVariant => _defaultVariant;
  public SysColGeneric.IReadOnlyDictionary<string, ModelWardrobeComponent> Components
    => new ReadOnlyDictionary<string, ModelWardrobeComponent>(_components);
  public SysColGeneric.IReadOnlyList<ModelWardrobeMaskRule> Masks
    => new ReadOnlyCollection<ModelWardrobeMaskRule>(_masks);

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (((PropertyUsageFlags)property["usage"].AsInt64()).HasFlag(PropertyUsageFlags.ScriptVariable))
      property["usage"] = (long)PropertyUsageFlags.Storage;
  }
}
