using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Wardrobe half of the model root: outfit selections and garment toggles update
/// garments and body masks together. Initialized writes preflight affected
/// garment and mask updates before applying them; each affected mask commits
/// one prepared selection that ORs the active rules' contributions while
/// retaining regions no rule controls; pieces and outfits are instance-scoped controls.
/// </summary>
public partial class CharacterModel
{
  public const string OutfitKey = "outfit";
  public const string ClothingEnabledKey = "clothing_enabled";
  public const string PiecesPrefix = "pieces/";

  // The import owns the catalog. It is deliberately absent from the root's
  // exported API: only per-instance selections are editable in Godot.
  internal ModelWardrobeConfiguration WardrobeConfiguration
  {
    get
    {
      if (_boundWardrobe is not null)
        return _boundWardrobe;
      Node? imported = GetNodeOrNull<Node>("Model");
      if (imported is null)
      {
        _catalogProvisional = true;
        return new ModelWardrobeConfiguration();
      }
      return (ModelWardrobeConfiguration)imported.GetMeta("wardrobe_catalog").AsGodotObject();
    }
  }

  private Godot.Collections.Dictionary<string, Variant> _selections = new();

  /// <summary>
  /// Per-instance selections keyed by control key — the serialized clothing
  /// control surface. Inspector edits replace the whole dictionary and apply
  /// immediately once initialized, rejection reverts; earlier they store as-is.
  /// </summary>
  [Export]
  public Godot.Collections.Dictionary<string, Variant> Selections
  {
    get => _selections;
    set
    {
      Godot.Collections.Dictionary<string, Variant> previous = _selections;
      _selections = value;
      if (!_wardrobeInitialized)
        return;
      try
      {
        ApplySelection(null, null);
      }
      catch
      {
        _selections = previous;
        throw;
      }
    }
  }

  private ModelWardrobeConfiguration? _boundWardrobe;
  private SysColGeneric.IReadOnlyDictionary<string, ModelWardrobeComponent> Components => _boundWardrobe!.Components;
  private SysColGeneric.List<(ModelWardrobeMaskRule Rule, MaskRuntime.Region Region)> _maskRules = [];
  private SysColGeneric.Dictionary<NodePath, Node3D> _garments = [];
  private readonly SysColGeneric.List<ModelClothingPiece> _pieces = [];
  private readonly SysColGeneric.List<ModelOutfit> _outfits = [];
  private bool _wardrobeInitialized;
  private bool _catalogProvisional;

  /// <summary>
  /// Binds the authored definitions and applies the stored selections once;
  /// the wardrobe publishes only after the complete combination preflights.
  /// </summary>
  private void InitializeWardrobe()
  {
    if (_wardrobeInitialized)
      return;
    EnsureWardrobeEntries();
    BindRequiredData();
    ApplySelection(null, null);
    _wardrobeInitialized = true;
  }

  public int OutfitIndex
  {
    get
    {
      EnsureWardrobeEntries();
      return Selections.TryGetValue(OutfitKey, out Variant stored) ? stored.AsInt32() : _boundWardrobe!.DefaultVariant;
    }
    set => WriteSelection(OutfitKey, Variant.From(value));
  }

  public bool ClothingEnabled
  {
    get
    {
      EnsureWardrobeEntries();
      return ReadSelectionBool(ClothingEnabledKey, true);
    }
    set => WriteSelection(ClothingEnabledKey, Variant.From(value));
  }

  /// <summary>
  /// Editor-only view of the stored <c>clothing_enabled</c> selection — never a
  /// second stored value. The read bypasses wardrobe binding so early editor
  /// probes cannot cache an empty wardrobe.
  /// </summary>
  public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
    =>
    [
      new()
      {
        ["name"] = ClothingEnabledKey,
        ["type"] = (long)Variant.Type.Bool,
        ["usage"] = (long)PropertyUsageFlags.Editor,
      },
    ];

  public override Variant _Get(StringName property)
    => property == ClothingEnabledKey
      ? Variant.From(ReadSelectionBool(ClothingEnabledKey, true))
      : default;

  /// <summary>Delegates to the existing setter, which derives and applies immediately.</summary>
  public override bool _Set(StringName property, Variant value)
  {
    if (property != ClothingEnabledKey)
      return false;
    ClothingEnabled = value.AsBool();
    return true;
  }

  /// <summary>Every clothing piece discovered from the configuration, in authored order.</summary>
  public SysColGeneric.IReadOnlyList<ModelClothingPiece> Pieces
  {
    get
    {
      EnsureWardrobeEntries();
      return _pieces.AsReadOnly();
    }
  }

  /// <summary>Every outfit discovered from the configured variants, in authored order.</summary>
  public SysColGeneric.IReadOnlyList<ModelOutfit> Outfits
  {
    get
    {
      EnsureWardrobeEntries();
      return _outfits.AsReadOnly();
    }
  }

  /// <summary>Looks a clothing piece up by its component name.</summary>
  public Option<ModelClothingPiece> FindPiece(StringName id)
  {
    EnsureWardrobeEntries();
    foreach (ModelClothingPiece piece in _pieces)
    {
      if (piece.Id == id)
        return Some(piece);
    }

    return None;
  }

  /// <summary>Looks an outfit up by its variant label.</summary>
  public Option<ModelOutfit> FindOutfit(StringName id)
  {
    EnsureWardrobeEntries();
    foreach (ModelOutfit outfit in _outfits)
    {
      if (outfit.Id == id)
        return Some(outfit);
    }

    return None;
  }

  /// <summary>The outfit currently selected on this model.</summary>
  public ModelOutfit CurrentOutfit
  {
    get
    {
      EnsureWardrobeEntries();
      int index = OutfitIndex;
      return index >= 0 && index < _outfits.Count
        ? _outfits[index]
        : throw new InvalidOperationException(
          $"The wardrobe '{Name}' has no outfit for the selected variant index {index}.");
    }
  }

  /// <summary>The stored per-piece selection.</summary>
  public bool GetPiece(StringName component)
  {
    EnsureWardrobeEntries();
    return ReadSelectionBool(PiecesPrefix + component, ComponentDefaultVisible(component));
  }

  /// <summary>Sets one piece's stored selection; unknown components reject in <see cref="ValidateSelection"/>.</summary>
  public void SetPiece(StringName component, bool enabled)
    => WriteSelection(PiecesPrefix + component, Variant.From(enabled));

  /// <summary>
  /// Sets one selection and applies its coordinated effect; pre-initialization
  /// writes store and derive on the next Initialize. Initialized writes preflight
  /// and apply immediately; hot reload restores selections while the root is
  /// inside the tree but not yet ready.
  /// </summary>
  private void WriteSelection(string key, Variant value)
  {
    EnsureWardrobeEntries();
    Variant selection = ValidateSelection(key, value);
    if (!_wardrobeInitialized)
    {
      Selections[key] = selection;
      return;
    }

    ApplySelection(key, selection);
  }

  // Materializes one stored selection per authored component from its authored
  // default so the inspector's Selections dictionary lists every toggleable
  // component: the stored values equal the fallbacks the derivation reads
  // anyway, so it changes nothing. Stored selections are never overwritten,
  // and outfit/clothing_enabled are not materialized — their absence is how
  // pre-initialization writes and rejected writes stay observable.
  private void MaterializeDefaultPieceSelections()
  {
    foreach (string component in Components.Keys)
    {
      string key = PiecesPrefix + component;
      if (!Selections.ContainsKey(key))
        Selections[key] = Variant.From(ComponentDefaultVisible(component));
    }
  }

  // Raw caller/saved selection values remain dynamic inputs, unlike definitions.
  private Variant ValidateSelection(string key, Variant value)
  {
    switch (key)
    {
      case OutfitKey:
        {
          int outfit = value.AsInt32();
          if (_outfits.Count > 0)
            outfit = Math.Clamp(outfit, 0, _outfits.Count - 1);
          return Variant.From(outfit);
        }
      case ClothingEnabledKey:
        return Variant.From(value.AsBool());
      default:
        {
          if (!key.StartsWith(PiecesPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"The wardrobe '{Name}' has no control '{key}'.");
          StringName component = key[PiecesPrefix.Length..];
          if (!Components.ContainsKey(component))
            throw new InvalidOperationException($"The wardrobe '{Name}' has no component '{component}'.");
          return Variant.From(value.AsBool());
        }
    }
  }

  // The authored visible default; unknown components default to visible.
  private bool ComponentDefaultVisible(StringName component)
    => !Components.TryGetValue(component, out ModelWardrobeComponent? info) || info.Visible;

  private bool ReadSelectionBool(string key, bool fallback)
    => Selections.TryGetValue(key, out Variant stored) ? stored.AsBool() : fallback;

  // One coordinated write: derive the complete proposed state and preflight its
  // live targets before anything is stored or written, so a rejection leaves
  // the stored selections, the garments, and the masks untouched; then store
  // the selection (initialized writes), materialize the authored piece defaults
  // for the inspector (initialization and whole-dictionary replacements), and
  // commit the garments and masks.
  private void ApplySelection(string? key, Variant? selection)
  {
    var (garments, masks) = DeriveChanges(key, selection);
    // Scene lifetime can change after import. Reject a freed live target before
    // touching selections or any other node; this is not authored-data validation.
    foreach ((Node node, _) in garments)
    {
      if (!GodotObject.IsInstanceValid(node))
        throw new InvalidOperationException("A wardrobe garment was freed while its model is still in use.");
    }
    if (key is not null)
      Selections[key] = selection!.Value;
    else
      MaterializeDefaultPieceSelections();
    foreach ((Node node, bool visible) in garments)
      node.Set("visible", Variant.From(visible));
    foreach (Action commit in masks)
      commit();
  }

  // One coordinated derivation over the proposed state — the stored selections
  // with `key` overridden by `selection`; a null key reads the stored state.
  // Garment visibility covers the affected components: every component for
  // initialization and outfit/clothing derivations, the named one for
  // pieces/<name>. Per affected mask runtime it prepares one selection seeded
  // from the stored states minus every region this wardrobe controls on that
  // mask, then every active rule's region ORs its contribution, so overlapping
  // regions never resolve last-rule-wins. A runtime's group collects every rule
  // bound to it — not only the rules this write affects — so a rule another
  // component controls still contributes from the proposed state, and no write
  // re-resolves mesh paths.
  private (SysColGeneric.List<(Node Node, bool Visible)> Garments,
    SysColGeneric.List<Action> Masks) DeriveChanges(
    string? key, Variant? selection)
  {
    bool global = key is null or OutfitKey or ClothingEnabledKey;
    string component = global ? "" : key![PiecesPrefix.Length..];
    int outfit = key == OutfitKey
      ? selection!.Value.AsInt32()
      : Selections.TryGetValue(OutfitKey, out Variant storedOutfit)
        ? storedOutfit.AsInt32() : _boundWardrobe!.DefaultVariant;
    bool clothing = key == ClothingEnabledKey
      ? selection!.Value.AsBool() : ReadSelectionBool(ClothingEnabledKey, true);
    bool Selected(string name) => key == PiecesPrefix + name
      ? selection!.Value.AsBool()
      : ReadSelectionBool(PiecesPrefix + name, ComponentDefaultVisible(name));

    // A rule contributes its bit when authored enabled, its variant matches the
    // proposed outfit (or is global), and its component is selected in the
    // proposed state with at least one garment matching the proposed outfit. A
    // rule without a component stays an authored global rule with no component
    // gating.
    bool RuleIsActive(ModelWardrobeMaskRule rule)
    {
      if (!rule.Enabled || (rule.Variant >= 0 && rule.Variant != outfit))
        return false;
      if (rule.Component.Length == 0)
        return true;
      if (!clothing || !Selected(rule.Component))
        return false;
      return HasGarmentForOutfit(rule.Component, outfit);
    }

    var garments = new SysColGeneric.List<(Node Node, bool Visible)>();
    void GarmentsFor(string affected)
    {
      bool enabled = clothing && Selected(affected);
      foreach (ModelWardrobeGarment piece in Components[affected].Pieces)
      {
        bool visible = enabled && (piece.Variant == -1 || piece.Variant == outfit);
        garments.Add((_garments[piece.Path], visible));
      }
    }

    if (global)
    {
      foreach (string affected in Components.Keys)
        GarmentsFor(affected);
    }
    else
    {
      GarmentsFor(component);
    }

    var masks = new SysColGeneric.List<Action>();
    foreach (MaskRuntime owner in _maskRules.AsValueEnumerable()
      .Where(binding => global || binding.Rule.Component == component)
      .Select(binding => binding.Region.Owner).Distinct())
    {
      // Re-derive every rule on an affected mask, including other components'
      // rules. Keep the first affected rule's commit order across masks.
      var rules = _maskRules.AsValueEnumerable().Where(binding => binding.Region.Owner == owner);
      masks.Add(owner.PrepareSelection(
        rules.Select(binding => binding.Region).ToArray(),
        rules.Where(binding => RuleIsActive(binding.Rule)).Select(binding => binding.Region).ToArray()));
    }

    return (garments, masks);
  }

  private bool HasGarmentForOutfit(string component, int outfit)
    => Components[component].Pieces.AsValueEnumerable()
      .Any(piece => piece.Variant == -1 || piece.Variant == outfit);

  // Stored selection, master switch, and outfit membership jointly determine visibility.
  internal bool ComponentActive(string component)
    => ClothingEnabled && GetPiece(component) && HasGarmentForOutfit(component, OutfitIndex);

  // The importer checks every garment and mask rule. Bind instance references
  // once; selections do not repeatedly resolve or validate fixed topology.
  private void BindRequiredData()
  {
    foreach (ModelWardrobeComponent component in Components.Values)
    {
      foreach (ModelWardrobeGarment piece in component.Pieces)
        _garments[piece.Path] = GetNode<Node3D>(piece.Path);
    }

    var bindings = new SysColGeneric.List<(ModelWardrobeMaskRule Rule, MaskRuntime.Region Region)>();
    foreach (ModelWardrobeMaskRule rule in _boundWardrobe!.Masks)
      bindings.Add((rule, ResolveMask(rule.Path).BindRegion(rule.Index)));
    _maskRules = bindings;
  }

  // Create instance-scoped controls without parsing or copying the shared definitions.
  private void EnsureWardrobeEntries()
  {
    if (_boundWardrobe is not null)
      return;
    ModelWardrobeConfiguration configuration = WardrobeConfiguration;
    _pieces.Clear();
    foreach (string component in configuration.Components.Keys)
      _pieces.Add(new ModelClothingPiece(this, component));
    _outfits.Clear();
    for (int i = 0; i < configuration.Variants.Count; i++)
      _outfits.Add(new ModelOutfit(this, configuration.Variants[i], i));
    _boundWardrobe = configuration;
  }
}
