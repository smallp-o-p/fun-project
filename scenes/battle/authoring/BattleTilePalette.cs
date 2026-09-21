using FunProject.Battle;
using Godot;
using Godot.Collections;

/// <summary>
/// A mapping of MeshLibrary objects to Tile data.
/// </summary>
[Tool, GlobalClass]
public partial class BattleTilePalette : Resource
{
  private MeshLibrary? _meshLibrary;
  [Export]
  public required MeshLibrary? MeshLibrary
  {
    get => _meshLibrary;
    set
    {
      _meshLibrary = value;
      NotifyPropertyListChanged();
    }
  }

  // Do not add any keys on your own: This is automatically filled with the required keys when the MeshLib is set. 
  [Export] public Dictionary<StringName, BattleMapTileData> Brushes { get; private set; } = [];

  public override void _ValidateProperty(Dictionary property)
  {
    if (MeshLibrary is null)
      return;

    if (property["name"].AsStringName() == PropertyName.MeshLibrary)
    {
      foreach (var meshId in MeshLibrary.GetItemList())
      {
        StringName name = MeshLibrary.GetItemName(meshId);

        if (Brushes.TryGetValue(name, out _))
          continue;
        Brushes[name] = new BattleMapTileData();
      }
    }
    else if (property["name"].AsStringName() == PropertyName.Brushes)
    {
      foreach (var (name, _) in Brushes)
      {
        if (MeshLibrary.FindItemByName(name) == -1)
        {
          GD.PushError($"Could not find item {name} inside mesh library!");
        }
      }
    }
  }
}