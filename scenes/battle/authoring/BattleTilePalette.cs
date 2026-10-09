using Godot;
using System;

namespace FunProject.Scenes;

// Palettes are ordinary MeshLibraries; native item IDs belong to their painted cells.
public static class BattleTilePalette
{
  public static Godot.Collections.Dictionary<int, Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>> ReadFootprints(MeshLibrary palette)
  {
    Godot.Collections.Dictionary<int, Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>> footprints = [];
    foreach (int item in palette.GetItemList())
    {
      var mesh = palette.GetItemMesh(item);
      if (mesh?.HasMeta(BattleProp.FootprintMeta) != true)
        throw new InvalidOperationException($"Palette item {item} needs an exported BattleProp mesh with footprint metadata.");
      footprints[item] = mesh.GetMeta(BattleProp.FootprintMeta).AsGodotDictionary<Godot.Vector3I, BattleFootprintData>();
    }
    return footprints;
  }
}
