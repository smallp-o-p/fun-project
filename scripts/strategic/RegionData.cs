using FunProject.Core;
using Godot;

namespace FunProject.Strategic;

// Authored region identity + metadata. The region's shape (and tint) is authored as scene
// nodes in the Godot editor under the geoscape map control, bound to this data by node name
// (each region Area2D's name must match a RegionData.Name) — the backend never sees geometry.
[GlobalClass]
public partial class RegionData : NamedEntityData
{
  [Export(PropertyHint.MultilineText)] public string FlavorText { get; set; } = "";
}
