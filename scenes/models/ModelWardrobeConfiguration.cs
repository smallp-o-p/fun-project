using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Fixed, shared wardrobe definitions. Validate authoring before saving generated
/// resources; the model's editor warnings also report invalid definitions.
/// Runtime instances bind these definitions without parsing or copying them.
/// </summary>
[Tool, GlobalClass]
public partial class ModelWardrobeConfiguration : Resource
{
  [Export] public string[] Variants { get; set; } = [];
  [Export] public int DefaultVariant { get; set; }
  [Export] public Godot.Collections.Dictionary<string, ModelWardrobeComponent> Components { get; set; } = new();
  [Export] public Godot.Collections.Array<ModelWardrobeMaskRule> Masks { get; set; } = [];

  /// <summary>Author-time schema checks; real node and region bindings belong to each instance.</summary>
  public void ValidateAuthoring()
  {
    var variants = new SysColGeneric.HashSet<string>();
    foreach (string variant in Variants)
    {
      if (!variants.Add(variant))
        throw new InvalidOperationException($"The wardrobe has a duplicate outfit id '{variant}'.");
    }
    if (Variants.Length > 0 && (DefaultVariant < 0 || DefaultVariant >= Variants.Length))
      throw new InvalidOperationException($"The wardrobe default variant {DefaultVariant} is outside its authored outfits.");

    void CheckVariant(int variant, string entry)
    {
      if (variant != -1 && (variant < 0 || variant >= Variants.Length))
        throw new InvalidOperationException($"The wardrobe {entry} has variant {variant} outside its authored outfits.");
    }

    foreach (var (name, component) in Components)
    {
      if (component is null)
        throw new InvalidOperationException($"The wardrobe component '{name}' has no definition.");
      foreach (ModelWardrobeGarment piece in component.Pieces)
      {
        if (piece is null || piece.Path.IsEmpty)
          throw new InvalidOperationException($"The wardrobe component '{name}' has a garment without a path.");
        CheckVariant(piece.Variant, $"component '{name}'");
      }
    }
    foreach (ModelWardrobeMaskRule rule in Masks)
    {
      if (rule is null || rule.Path.IsEmpty || rule.Name.IsEmpty || rule.Index < 0)
        throw new InvalidOperationException("The wardrobe has an incomplete mask rule.");
      if (rule.Component.Length > 0 && !Components.ContainsKey(rule.Component))
        throw new InvalidOperationException($"The wardrobe mask rule '{rule.Name}' references unknown component '{rule.Component}'.");
      CheckVariant(rule.Variant, $"mask rule '{rule.Name}'");
    }
  }
}
