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
  /// identity (resource path, or instance id when pathless); entries hold their
  /// active render mesh strongly, so eviction never pulls it out from under an instance.
  /// </summary>
  private static readonly SysColGeneric.Dictionary<string, VariantCache> VariantCacheByConfiguration = new();

  private sealed class VariantCache
  {
    internal SysColGeneric.Dictionary<long, ArrayMesh> Variants = new();
    internal SysColGeneric.List<long> Order = new();
  }

  private Godot.Collections.Array<ModelMeshMaskSetup> _maskSetups = new();

  /// <summary>
  /// Authored mask setups, each binding a root-relative mesh path to its typed
  /// configuration; a late non-null assignment to a ready, uninitialized model
  /// retries <see cref="Initialize"/>.
  /// </summary>
  [Export]
  public Godot.Collections.Array<ModelMeshMaskSetup> MaskSetups
  {
    get => _maskSetups;
    set
    {
      _maskSetups = value;
      if (!_initialized && IsNodeReady() && value is not null)
        Initialize();
    }
  }

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
    /// Resolves one authored setup into a runtime; body, configuration, geometry,
    /// and first render all validate here, so a rejected setup publishes no handle.
    /// </summary>
    internal MaskRuntime(CharacterModel model, NodePath meshPath, ModelMeshMaskSetup setup)
    {
      Model = model;
      MeshPath = meshPath;
      ModelMaskConfiguration configuration = setup.Configuration
        ?? throw new InvalidOperationException(
          $"The character model '{model.Name}' mask setup '{meshPath}' has no mask source configuration.");
      _body = model.GetNodeOrNull<MeshInstance3D>(meshPath)
        ?? throw new InvalidOperationException(
          $"The character model '{model.Name}' mask setup mesh path '{meshPath}' does not resolve to a MeshInstance3D.");
      _fullMesh = _body.Mesh as ArrayMesh
        ?? throw new InvalidOperationException(
          $"The character model '{model.Name}' mask setup '{meshPath}' body does not carry an editable ArrayMesh.");
      ParseConfiguration(configuration);
      if (_triangleMasks.Count != _fullMesh.GetSurfaceCount())
        throw new InvalidOperationException(
          $"The character model '{model.Name}' mask setup '{meshPath}' configuration supplies {_triangleMasks.Count} triangle-mask surfaces for a mesh with {_fullMesh.GetSurfaceCount()} surfaces.");
      for (int surface = 0; surface < _triangleMasks.Count; surface++)
      {
        int[] indices = _fullMesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (_triangleMasks[surface].Length * 3 != indices.Length)
          throw new InvalidOperationException(
            $"The character model '{model.Name}' mask setup '{meshPath}' configuration supplies {_triangleMasks[surface].Length} triangle-mask entries for surface {surface} of a mesh with {indices.Length / 3} triangles.");
      }

      _cacheKey = $"{_fullMesh.GetInstanceId()}|{ConfigurationKey(configuration)}";
      if (!VariantCacheByConfiguration.ContainsKey(_cacheKey))
      {
        if (ModelMaskGeometry.GeometryHash(_fullMesh) != configuration.GeometryHash)
          throw new InvalidOperationException(
            $"The mask configuration '{configuration.ResourcePath}' is incompatible with the body mesh of '{model.Name}' at '{meshPath}': "
            + "it was generated for different geometry, so its triangle masks do not describe this mesh and it must be regenerated for it.");
        VariantCacheByConfiguration[_cacheKey] = new VariantCache();
      }

      ApplyCurrent(_enabledBits);
    }

    private CharacterModel Model { get; }

    private NodePath MeshPath { get; }

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
        bits |= 1L << BitIndexOf(region);
      return bits;
    }

    /// <summary>Selects the complete enabled region set; foreign entries reject before any write.</summary>
    internal void SetRegions(System.Collections.Generic.IEnumerable<Region> enabled)
      => WriteStates(BitsOf(enabled));

    /// <summary>
    /// Prepares one coordinated update — stored states minus controlled regions
    /// plus active ones — so uncontrolled regions survive and overlapping rules
    /// OR together; foreign or fabricated entries reject before any state changes.
    /// </summary>
    internal Action PrepareSelection(
      System.Collections.Generic.IEnumerable<Region> controlled,
      System.Collections.Generic.IEnumerable<Region> active)
    {
      long bits = (_enabledBits & ~BitsOf(controlled)) | BitsOf(active);
      return () => WriteStates(bits);
    }

    /// <summary>
    /// Binds an authored rule to this mask's region entry: the authored name
    /// must match the configured mask at the authored index.
    /// </summary>
    internal Region BindRegion(StringName name, int index)
    {
      if (index < 0 || index >= _regions.Count)
        throw new InvalidOperationException(
          $"The wardrobe '{Model.Name}' mask rule '{name}' targets index {index} outside the {_regions.Count} masks of '{MeshPath}'.");
      Region region = _regions[index];
      if (region.Name != name)
        throw new InvalidOperationException(
          $"The wardrobe '{Model.Name}' mask rule '{name}' does not match the configured mask name '{region.Name}' at index {index} of '{MeshPath}'.");
      return region;
    }

    private void WriteStates(long bits)
    {
      _enabledBits = bits;
      if (Model.IsNodeReady())
        ApplyCurrent(bits);
    }

    // Region-table membership is the ownership guard: foreign runtimes' entries
    // and fabricated entries are absent from the minted table.
    private int BitIndexOf(Region region)
    {
      ArgumentNullException.ThrowIfNull(region);
      int index = region.Index;
      if (index < 0 || index >= _regions.Count || !ReferenceEquals(_regions[index], region))
        throw new InvalidOperationException(
          $"The mask region '{region.Name}' was not minted by the mesh mask '{MeshPath}' of '{Model.Name}'.");
      return index;
    }

    /// <summary>The minted region entry of the configured mask <paramref name="name"/>.</summary>
    internal Region RegionOf(StringName name)
    {
      foreach (Region region in _regions)
      {
        if (region.Name == name)
          return region;
      }

      throw new InvalidOperationException(
        $"The mesh mask '{MeshPath}' has no configured mask '{name}'.");
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

    private static string ConfigurationKey(ModelMaskConfiguration configuration)
    {
      NodePath path = configuration.ResourcePath;
      return path.IsEmpty ? $"#{configuration.GetInstanceId()}" : path.ToString();
    }

    private void ParseConfiguration(ModelMaskConfiguration configuration)
    {
      if (configuration.Masks is null)
        throw MissingConfigurationField(nameof(configuration.Masks));
      if (configuration.TriangleMasks is null)
        throw MissingConfigurationField(nameof(configuration.TriangleMasks));

      var regions = new SysColGeneric.List<Region>();
      foreach (SysColGeneric.KeyValuePair<string, bool> entry in configuration.Masks)
      {
        if (entry.Value)
          _enabledBits |= 1L << regions.Count;
        regions.Add(new Region(this, entry.Key, regions.Count));
      }
      if (regions.Count > 64)
        throw new InvalidOperationException(
          $"The mesh mask '{MeshPath}' configuration defines {regions.Count} masks; the mask bitset holds at most 64.");
      long validBits = regions.Count == 64 ? -1L : (1L << regions.Count) - 1;

      _defaultBits = configuration.DefaultBits;
      if ((_defaultBits & ~validBits) != 0)
        throw new InvalidOperationException(
          $"The mesh mask '{MeshPath}' configuration default bits {_defaultBits} select masks beyond the {regions.Count} configured masks.");
      _defaultMesh = configuration.DefaultMesh;

      foreach (long[] flags in configuration.TriangleMasks)
      {
        foreach (long flag in flags)
        {
          if ((flag & ~validBits) != 0)
            throw new InvalidOperationException(
              $"The mesh mask '{MeshPath}' configuration has a triangle flag {flag} selecting masks beyond the {regions.Count} configured masks.");
        }

        _triangleMasks.Add(flags);
      }

      _regions = regions;
    }

    private InvalidOperationException MissingConfigurationField(string field) => new(
      $"The mesh mask '{MeshPath}' configuration has no '{field}' field.");
  }

  /// <summary>
  /// Builds one runtime per authored setup, publishing the registry only after
  /// every runtime resolved; duplicate mesh paths reject.
  /// </summary>
  private void InitializeMasks()
  {
    var entries = new SysColGeneric.Dictionary<NodePath, MaskRuntime>();
    foreach (ModelMeshMaskSetup setup in MaskSetups)
    {
      NodePath meshPath = setup.MeshPath;
      if (meshPath.IsEmpty)
        throw new InvalidOperationException(
          $"The character model '{Name}' has a mask setup without a mesh path.");
      if (entries.ContainsKey(meshPath))
        throw new InvalidOperationException(
          $"The character model '{Name}' has more than one mask setup for mesh path '{meshPath}'.");
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
  internal MaskRuntime ResolveMask(NodePath meshPath)
  {
    if (_maskEntries.TryGetValue(meshPath, out MaskRuntime? entry))
      return entry;
    throw new InvalidOperationException(
      $"The character model '{Name}' mask path '{meshPath}' does not resolve to a configured mask.");
  }
}
