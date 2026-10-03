using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Masking half of the model root: render-only mesh masking over the authored
/// <see cref="ModelMeshMaskSetup"/> entries — the source mesh, its blend-shape
/// channels, and its skin stay editable while only the render base swaps to a
/// compacted variant; mask states are per-instance and never serialized.
/// </summary>
public partial class CharacterModel
{
  /// <summary>
  /// Shared generated-variant cache, keyed by source mesh and configuration
  /// identity; entries hold their
  /// active render mesh strongly, so eviction never pulls it out from under an instance.
  /// </summary>
  private static readonly SysColGeneric.Dictionary<string, VariantCache> VariantCacheByConfiguration = new();

  private sealed class VariantCache
  {
    internal SysColGeneric.Dictionary<long, ArrayMesh> Variants = new();
    internal SysColGeneric.List<long> Order = new();
  }

  // The import compiles fixed paths/configurations from the same source identities
  // it validates. Instances bind them directly; there is no editable wrapper slot.
  internal SysColGeneric.IReadOnlyList<ModelMeshMaskSetup> MaskSetups
    => WardrobeConfiguration.MaskSetups;

  // Plain field on purpose: hot reload persists plain fields but empties
  // field-initialized readonly collections; a repeated Initialize rebuilds the registry.
  private SysColGeneric.Dictionary<NodePath, MaskRuntime> _maskEntries = new();

  /// <summary>
  /// Runtime state of one resolved mesh/configuration pair: it mints the region
  /// entries callers select and keeps the bit encoding private.
  /// </summary>
  internal sealed class MaskRuntime
  {
    /// <summary>One region entry minted and owned by its mask runtime.</summary>
    public sealed class Region
    {
      internal Region(MaskRuntime owner, StringName name, int index)
      {
        Owner = owner;
        Name = name;
        Index = index;
      }

      /// <summary>The runtime that minted this entry.</summary>
      internal MaskRuntime Owner { get; }

      /// <summary>The configured mask name this region was minted from.</summary>
      public StringName Name { get; }

      /// <summary>Whether this region is enabled on its owning mask.</summary>
      public bool Enabled => (Owner._enabledBits & (1L << Index)) != 0;

      internal int Index { get; }
    }

    /// <summary>
    /// Resolves one authored setup into a runtime; the import has already
    /// validated the configuration and geometry. Only instance binding happens here.
    /// </summary>
    internal MaskRuntime(CharacterModel model, NodePath meshPath, ModelMeshMaskSetup setup)
    {
      Model = model;
      ModelMaskConfiguration configuration = setup.Configuration!;
      _body = model.GetNode<MeshInstance3D>(meshPath);
      _fullMesh = (ArrayMesh)_body.Mesh;
      BindConfiguration(configuration);
      _cacheKey = $"{_fullMesh.GetInstanceId()}|{configuration.GetInstanceId()}";
      if (!VariantCacheByConfiguration.ContainsKey(_cacheKey))
        VariantCacheByConfiguration[_cacheKey] = new VariantCache();

      ApplyCurrent(_enabledBits);
    }

    private CharacterModel Model { get; }

    /// <summary>Current render mesh.</summary>
    internal ArrayMesh? RenderMesh { get; private set; }

    /// <summary>The minted region entries in configured bit order; read-only inventory.</summary>
    internal SysColGeneric.IReadOnlyList<Region> Regions => _regions.AsReadOnly();

    // Plain fields on purpose: hot reload persists plain fields but empties
    // field-initialized readonly collections, so these caches and states survive.
    private SysColGeneric.List<Region> _regions = new();
    private SysColGeneric.List<long[]> _triangleMasks = new();
    private long _enabledBits;
    private MeshInstance3D _body;
    private ArrayMesh _fullMesh;
    private long _defaultBits;
    private ArrayMesh? _defaultMesh;
    private string _cacheKey = "";

    // Null until the first render, not a sentinel bitset: the all-64-enabled
    // combination is the valid bitset -1 and must still render.
    private long? _lastBits;

    private long BitsOf(System.Collections.Generic.IEnumerable<Region> regions)
    {
      long bits = 0;
      foreach (Region region in regions)
        bits |= 1L << region.Index;
      return bits;
    }

    /// <summary>
    /// Prepares one coordinated update — stored states minus controlled regions
    /// plus active ones — so uncontrolled regions survive and overlapping rules
    /// OR together. Rule bindings were compiled and checked at import.
    /// </summary>
    internal Action PrepareSelection(
      System.Collections.Generic.IEnumerable<Region> controlled,
      System.Collections.Generic.IEnumerable<Region> active)
    {
      // A scene may be torn down after import. Check the live target before the
      // wardrobe commits any selections, garments, or masks.
      if (!GodotObject.IsInstanceValid(_body))
        throw new InvalidOperationException("A masked body was freed while its model is still in use.");
      long bits = (_enabledBits & ~BitsOf(controlled)) | BitsOf(active);
      return () => WriteStates(bits);
    }

    /// <summary>
    /// Binds an import-checked rule to this instance's region entry.
    /// </summary>
    internal Region BindRegion(int index) => _regions[index];

    private void WriteStates(long bits)
    {
      _enabledBits = bits;
      if (Model.IsNodeReady())
        ApplyCurrent(bits);
    }

    private void ApplyCurrent(long bits)
    {
      if (_lastBits == bits)
        return;
      RenderMesh = bits switch
      {
        0 => _fullMesh,
        _ when bits == _defaultBits && _defaultMesh is not null => _defaultMesh,
        _ => GetOrBuildVariant(bits),
      };
      RenderingServer.InstanceSetBase(_body.GetInstance(), RenderMesh.GetRid());
      for (int s = 0; s < _fullMesh.GetSurfaceCount(); s++)
        ApplySurfaceOverride(s, bits);
      for (int i = 0; i < _body.GetBlendShapeCount(); i++)
        RenderingServer.InstanceSetBlendShapeWeight(_body.GetInstance(), i, _body.GetBlendShapeValue(i));
      _lastBits = bits;
    }

    private ArrayMesh GetOrBuildVariant(long bits)
    {
      VariantCache cache = VariantCacheByConfiguration[_cacheKey];
      if (cache.Variants.TryGetValue(bits, out ArrayMesh? variant))
        return variant;
      variant = ModelMaskGeometry.BuildMesh(_fullMesh, _triangleMasks, bits);
      cache.Variants[bits] = variant;
      cache.Order.Add(bits);
      if (cache.Order.Count > ModelMaskGeometry.GeneratedVariantCacheBound)
      {
        cache.Variants.Remove(cache.Order[0]);
        cache.Order.RemoveAt(0);
      }

      return variant;
    }

    private void ApplySurfaceOverride(int surface, long bits)
    {
      Material? material = _body.GetSurfaceOverrideMaterial(surface);
      if (material is null)
        return;
      RenderingServer.InstanceSetSurfaceOverrideMaterial(_body.GetInstance(), surface, material.GetRid());
      // Materials without the authored outline pass are common (a meta read on a
      // missing key logs a native error), so the NextPass is probed by pattern first.
      if (material.NextPass is not ShaderMaterial outline || !outline.HasMeta("source_weights"))
        return;
      float[] sourceWeights = outline.GetMeta("source_weights").AsFloat32Array();
      float[] values = sourceWeights;
      if (bits != 0)
      {
        int[] sourceIndices = RenderMesh!.GetMeta("source_vertex_indices").AsGodotArray()[surface].AsInt32Array();
        values = new float[sourceIndices.Length];
        for (int i = 0; i < sourceIndices.Length; i++)
          values[i] = sourceWeights[sourceIndices[i]];
      }

      outline.SetShaderParameter("vertex_weights", ModelMaskGeometry.WeightTexture(values));
    }

    private void BindConfiguration(ModelMaskConfiguration configuration)
    {
      foreach (SysColGeneric.KeyValuePair<string, bool> entry in configuration.Masks)
      {
        if (entry.Value)
          _enabledBits |= 1L << _regions.Count;
        _regions.Add(new Region(this, entry.Key, _regions.Count));
      }
      _defaultBits = configuration.DefaultBits;
      _defaultMesh = configuration.DefaultMesh;
      foreach (long[] flags in configuration.TriangleMasks)
        _triangleMasks.Add(flags);
    }
  }

  /// <summary>
  /// Builds one runtime per authored setup, publishing the registry only after
  /// every runtime is bound. Import-generated paths are unique.
  /// </summary>
  private void InitializeMasks()
  {
    var entries = new SysColGeneric.Dictionary<NodePath, MaskRuntime>();
    foreach (ModelMeshMaskSetup setup in MaskSetups)
    {
      NodePath meshPath = setup.MeshPath;
      entries[meshPath] = new MaskRuntime(this, meshPath, setup);
    }

    _maskEntries = entries;
  }

  /// <summary>The render mesh the mask over <paramref name="meshPath"/> currently draws.</summary>
  internal ArrayMesh? MaskRenderMesh(NodePath meshPath) => ResolveMask(meshPath).RenderMesh;

  /// <summary>
  /// Resolves the runtime of the mask covering <paramref name="meshPath"/>, the
  /// authoring/initialization-boundary handle for its minted region entries.
  /// </summary>
  internal MaskRuntime ResolveMask(NodePath meshPath) => _maskEntries[meshPath];
}
