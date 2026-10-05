using System.Collections.ObjectModel;
using Godot;

namespace FunProject.Models;

/// <summary>One imported clothing toggle and its fixed outfit-specific garment nodes.</summary>
[Tool]
public partial class ModelWardrobeComponent : Resource
{
  [Export] private bool _visible = true;
  [Export] private Godot.Collections.Array<ModelWardrobeGarment> _pieces = [];

  public bool Visible => _visible;
  public SysColGeneric.IReadOnlyList<ModelWardrobeGarment> Pieces
    => new ReadOnlyCollection<ModelWardrobeGarment>(_pieces);

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (((PropertyUsageFlags)property["usage"].AsInt64()).HasFlag(PropertyUsageFlags.ScriptVariable))
      property["usage"] = (long)PropertyUsageFlags.Storage;
  }
}
