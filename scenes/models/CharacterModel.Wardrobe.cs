using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Wardrobe half of the model root: outfit selections and garment toggles update
/// garments and body masks together. Initialized writes preflight affected
/// garment and mask updates before applying them; each affected mask commits
/// one prepared selection that ORs the active rules' contributions while
/// retaining regions no rule controls; pieces and outfits are owner-bound handles.
/// </summary>
public partial class CharacterModel
{
  public const string OutfitKey = "outfit";
  public const string ClothingEnabledKey = "clothing_enabled";
  public const string PiecesPrefix = "pieces/";

  private readonly record struct WardrobePiece(NodePath Path, int Variant);

  private sealed class WardrobeComponent
  {
    internal required bool Visible { get; init; }
    internal required SysColGeneric.List<WardrobePiece> Pieces { get; init; }
  }

  /// <summary>
  /// One authored mask rule; its path/name/index triple binds to a region entry
  /// during initialization, and a retried initialization rebinds.
  /// </summary>
  private sealed class MaskRule
  {
    internal required NodePath Path { get; init; }
    internal required StringName Name { get; init; }
    internal required int Index { get; init; }
    internal required string Component { get; init; }
    internal required bool Enabled { get; init; }
    internal required int Variant { get; init; }
    internal MaskRuntime.Region? Region { get; set; }
  }

  [Export] public Godot.Collections.Dictionary WardrobeConfiguration { get; set; } = new();

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

  private int _defaultVariant;
  private readonly SysColGeneric.Dictionary<string, WardrobeComponent> _components = new();
  private readonly SysColGeneric.List<MaskRule> _maskRules = new();
  private readonly SysColGeneric.List<ModelClothingPiece> _pieces = [];
  private readonly SysColGeneric.List<ModelOutfit> _outfits = [];
  private bool _parsed;
  private bool _wardrobeInitialized;

  /// <summary>
  /// Parses the authored configuration and applies the stored selections once;
  /// the wardrobe publishes only after the complete combination preflights.
  /// </summary>
  private void InitializeWardrobe()
  {
    if (_wardrobeInitialized)
      return;
    EnsureParsed();
    BindRequiredData();
    ApplySelection(null, null);
    _wardrobeInitialized = true;
  }

  public int OutfitIndex
  {
    get
    {
      EnsureParsed();
      return Selections.TryGetValue(OutfitKey, out Variant stored) ? stored.AsInt32() : _defaultVariant;
    }
    set => WriteSelection(OutfitKey, Variant.From(value));
  }

  public bool ClothingEnabled
  {
    get
    {
      EnsureParsed();
      return ReadSelectionBool(ClothingEnabledKey, true);
    }
    set => WriteSelection(ClothingEnabledKey, Variant.From(value));
  }

  /// <summary>
  /// Editor-only view of the stored <c>clothing_enabled</c> selection — never a
  /// second stored value. The read bypasses wardrobe parsing so early editor
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
      EnsureParsed();
      return _pieces;
    }
  }

  /// <summary>Every outfit discovered from the configured variants, in authored order.</summary>
  public SysColGeneric.IReadOnlyList<ModelOutfit> Outfits
  {
    get
    {
      EnsureParsed();
      return _outfits;
    }
  }

  /// <summary>Looks a clothing piece up by its component name.</summary>
  public Option<ModelClothingPiece> FindPiece(StringName id)
  {
    EnsureParsed();
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
    EnsureParsed();
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
      EnsureParsed();
      int index = OutfitIndex;
      return index >= 0 && index < _outfits.Count
        ? _outfits[index]
        : throw new InvalidOperationException(
          $"The wardrobe '{Name}' has no outfit for the selected variant index {index}.");
    }
  }

  /// <summary>Selects an outfit discovered by this model; foreign outfits are rejected.</summary>
  public void SelectOutfit(ModelOutfit outfit)
  {
    ValidateOwnership(outfit);
    WriteSelection(OutfitKey, Variant.From(outfit.VariantIndex));
  }

  /// <summary>Enables or disables a piece discovered by this model; foreign pieces are rejected.</summary>
  public void SetPieceEnabled(ModelClothingPiece piece, bool enabled)
  {
    ValidateOwnership(piece);
    WriteSelection(PiecesPrefix + piece.Id, Variant.From(enabled));
  }

  // Foreign entries are caller bugs: reject them before any selection indexing.
  private void ValidateOwnership(ModelClothingPiece piece)
  {
    ArgumentNullException.ThrowIfNull(piece);
    if (!ReferenceEquals(piece.Owner, this))
      throw new InvalidOperationException(
        $"The clothing piece '{piece.Id}' belongs to the wardrobe '{piece.Owner.Name}', not '{Name}'.");
  }

  private void ValidateOwnership(ModelOutfit outfit)
  {
    ArgumentNullException.ThrowIfNull(outfit);
    if (!ReferenceEquals(outfit.Owner, this))
      throw new InvalidOperationException(
        $"The outfit '{outfit.Id}' belongs to the wardrobe '{outfit.Owner.Name}', not '{Name}'.");
  }

  /// <summary>The stored per-piece selection.</summary>
  public bool GetPiece(StringName component)
  {
    EnsureParsed();
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
    EnsureParsed();
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
    foreach (string component in _components.Keys)
    {
      string key = PiecesPrefix + component;
      if (!Selections.ContainsKey(key))
        Selections[key] = Variant.From(ComponentDefaultVisible(component));
    }
  }

  // Validates one raw selection write and returns its stored form.
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
          if (!_components.ContainsKey(component))
            throw new InvalidOperationException($"The wardrobe '{Name}' has no component '{component}'.");
          return Variant.From(value.AsBool());
        }
    }
  }

  // The authored visible default; unknown components default to visible.
  private bool ComponentDefaultVisible(StringName component)
    => !_components.TryGetValue(component, out WardrobeComponent? info) || info.Visible;

  private bool ReadSelectionBool(string key, bool fallback)
    => Selections.TryGetValue(key, out Variant stored) ? stored.AsBool() : fallback;

  // One coordinated write: derive the complete proposed state and resolve its
  // garment nodes before anything is stored or written, so a rejection leaves
  // the stored selections, the garments, and the masks untouched; then store
  // the selection (initialized writes), materialize the authored piece defaults
  // for the inspector (initialization and whole-dictionary replacements), and
  // commit the garments and masks.
  private void ApplySelection(string? key, Variant? selection)
  {
    var (garments, masks) = DeriveChanges(key, selection);
    foreach ((NodePath path, _) in garments)
      ResolveGarment(path);
    if (key is not null)
      Selections[key] = selection!.Value;
    else
      MaterializeDefaultPieceSelections();
    foreach ((NodePath path, bool visible) in garments)
      GetNode<Node>(path).Set("visible", Variant.From(visible));
    foreach (Action commit in masks.Values)
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
  private (SysColGeneric.List<(NodePath Path, bool Visible)> Garments,
    SysColGeneric.Dictionary<MaskRuntime, Action> Masks) DeriveChanges(
    string? key, Variant? selection)
  {
    bool global = key is null or OutfitKey or ClothingEnabledKey;
    string component = global ? "" : key![PiecesPrefix.Length..];
    int outfit = key == OutfitKey
      ? selection!.Value.AsInt32()
      : Selections.TryGetValue(OutfitKey, out Variant storedOutfit)
        ? storedOutfit.AsInt32() : _defaultVariant;
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
    bool RuleIsActive(MaskRule rule)
    {
      if (!rule.Enabled || (rule.Variant >= 0 && rule.Variant != outfit))
        return false;
      if (rule.Component.Length == 0)
        return true;
      if (!clothing || !Selected(rule.Component))
        return false;
      foreach (WardrobePiece piece in _components[rule.Component].Pieces)
      {
        if (piece.Variant == -1 || piece.Variant == outfit)
          return true;
      }

      return false;
    }

    var garments = new SysColGeneric.List<(NodePath Path, bool Visible)>();
    void GarmentsFor(string affected)
    {
      bool enabled = clothing && Selected(affected);
      foreach (WardrobePiece piece in _components[affected].Pieces)
      {
        bool visible = enabled && (piece.Variant == -1 || piece.Variant == outfit);
        garments.Add((piece.Path, visible));
      }
    }

    if (global)
    {
      foreach (string affected in _components.Keys)
        GarmentsFor(affected);
    }
    else
    {
      GarmentsFor(component);
    }

    var groups = new SysColGeneric.Dictionary<MaskRuntime,
      (SysColGeneric.List<MaskRuntime.Region> Controlled, SysColGeneric.List<MaskRuntime.Region> Active)>();
    // Affected owners first: a runtime any affected rule binds to is fully
    // re-derived below, so earlier unaffected rules of the same owner keep
    // contributing from the proposed state instead of vanishing when a later
    // rule's component created the group. Owners no affected rule binds to
    // stay untouched by this write.
    foreach (MaskRule rule in _maskRules)
    {
      if (!global && rule.Component != component)
        continue;
      MaskRuntime owner = rule.Region!.Owner;
      if (!groups.ContainsKey(owner))
        groups[owner] = (new SysColGeneric.List<MaskRuntime.Region>(),
          new SysColGeneric.List<MaskRuntime.Region>());
    }

    foreach (MaskRule rule in _maskRules)
    {
      if (!groups.TryGetValue(rule.Region!.Owner, out var group))
        continue;
      group.Controlled.Add(rule.Region);
      if (RuleIsActive(rule))
        group.Active.Add(rule.Region);
    }

    var masks = new SysColGeneric.Dictionary<MaskRuntime, Action>();
    foreach ((MaskRuntime owner,
      (SysColGeneric.List<MaskRuntime.Region> controlled,
        SysColGeneric.List<MaskRuntime.Region> active)) in groups)
    {
      masks[owner] = owner.PrepareSelection(controlled, active);
    }

    return (garments, masks);
  }

  private bool ComponentEnabled(StringName component)
    => ClothingEnabled && ReadSelectionBool(PiecesPrefix + component, ComponentDefaultVisible(component));

  // Effective visibility of one component's garments in the stored state: the
  // piece is selected, clothing is enabled, and the current outfit owns at
  // least one of its garments.
  internal bool ComponentActive(string component)
  {
    if (!ComponentEnabled(component))
      return false;
    foreach (WardrobePiece piece in _components[component].Pieces)
    {
      if (piece.Variant == -1 || piece.Variant == OutfitIndex)
        return true;
    }

    return false;
  }

  // Garment pieces are required data: a missing node is invalid authored data and
  // must reject before any selection is stored or visibility applied.
  private Node ResolveGarment(NodePath path)
    => GetNodeOrNull<Node>(path)
      ?? throw new InvalidOperationException(
        $"The character model '{Name}' garment piece path '{path}' does not resolve to a node.");

  // Setup-time required-data binding: every garment node resolves, and every
  // rule binds to a region entry minted by its mask's resolved runtime (path,
  // name, and index validate there) before the first derivation runs. The
  // bindings publish only after every rule resolved, and a retried Initialize
  // rebinds to the newly created runtimes.
  private void BindRequiredData()
  {
    foreach (WardrobeComponent component in _components.Values)
    {
      foreach (WardrobePiece piece in component.Pieces)
        ResolveGarment(piece.Path);
    }

    var bindings = new SysColGeneric.List<(MaskRule Rule, MaskRuntime.Region Region)>();
    foreach (MaskRule rule in _maskRules)
      bindings.Add((rule, ResolveMask(rule.Path).BindRegion(rule.Name, rule.Index)));
    foreach ((MaskRule rule, MaskRuntime.Region region) in bindings)
      rule.Region = region;
  }

  private void EnsureParsed()
  {
    if (_parsed)
      return;
    ParseConfiguration();
    _parsed = true;
  }

  private void ParseConfiguration()
  {
    var variants = new SysColGeneric.List<string>();
    int defaultVariant = 0;
    var components = new SysColGeneric.Dictionary<string, WardrobeComponent>();
    var maskRules = new SysColGeneric.List<MaskRule>();

    if (WardrobeConfiguration.Count > 0)
    {
      if (WardrobeConfiguration.ContainsKey("variants"))
      {
        var seenVariants = new SysColGeneric.HashSet<string>();
        foreach (Variant variant in WardrobeConfiguration["variants"].AsGodotArray())
        {
          string label = variant.AsString();
          if (!seenVariants.Add(label))
            throw new InvalidOperationException(
              $"The wardrobe '{Name}' configuration has a duplicate outfit id '{label}'.");
          variants.Add(label);
        }
      }

      if (WardrobeConfiguration.ContainsKey("default_variant"))
      {
        defaultVariant = WardrobeConfiguration["default_variant"].AsInt32();
        if (variants.Count > 0 && (defaultVariant < 0 || defaultVariant >= variants.Count))
          throw new InvalidOperationException(
            $"The wardrobe '{Name}' configuration default_variant {defaultVariant} is outside the {variants.Count} authored variants.");
      }

      if (!WardrobeConfiguration.ContainsKey("components"))
        throw new InvalidOperationException($"The wardrobe '{Name}' configuration has no 'components' dictionary.");
      foreach (SysColGeneric.KeyValuePair<Variant, Variant> entry
        in WardrobeConfiguration["components"].AsGodotDictionary())
      {
        string name = entry.Key.AsString();
        Godot.Collections.Dictionary info = entry.Value.AsGodotDictionary();
        var pieces = new SysColGeneric.List<WardrobePiece>();
        if (info.ContainsKey("pieces"))
        {
          foreach (Variant pieceVariant in info["pieces"].AsGodotArray())
          {
            Godot.Collections.Dictionary piece = pieceVariant.AsGodotDictionary();
            if (!piece.ContainsKey("path"))
              throw new InvalidOperationException(
                $"The wardrobe '{Name}' component '{name}' has a piece without a 'path'.");
            int variant = piece.ContainsKey("variant") ? piece["variant"].AsInt32() : -1;
            if (variant != -1 && (variant < 0 || variant >= variants.Count))
              throw new InvalidOperationException(
                $"The wardrobe '{Name}' component '{name}' has a piece with variant {variant} outside the {variants.Count} authored variants.");
            pieces.Add(new WardrobePiece(new NodePath(piece["path"].AsString()), variant));
          }
        }

        bool visible = !info.ContainsKey("visible") || info["visible"].AsBool();
        components[name] = new WardrobeComponent { Visible = visible, Pieces = pieces };
      }

      if (!WardrobeConfiguration.ContainsKey("masks"))
        throw new InvalidOperationException($"The wardrobe '{Name}' configuration has no 'masks' array.");
      foreach (Variant ruleVariant in WardrobeConfiguration["masks"].AsGodotArray())
      {
        Godot.Collections.Dictionary rule = ruleVariant.AsGodotDictionary();
        if (!rule.ContainsKey("path") || !rule.ContainsKey("name") || !rule.ContainsKey("index")
            || !rule.ContainsKey("component") || !rule.ContainsKey("enabled"))
          throw new InvalidOperationException(
            $"The wardrobe '{Name}' configuration has an incomplete mask rule entry.");
        string component = rule["component"].AsString();
        if (component.Length > 0 && !components.ContainsKey(component))
          throw new InvalidOperationException(
            $"The wardrobe '{Name}' mask rule '{rule["name"]}' references unknown component '{component}'.");
        int variant = rule.ContainsKey("variant") ? rule["variant"].AsInt32() : -1;
        if (variant != -1 && (variant < 0 || variant >= variants.Count))
          throw new InvalidOperationException(
            $"The wardrobe '{Name}' mask rule '{rule["name"]}' has variant {variant} outside the {variants.Count} authored variants.");
        maskRules.Add(new MaskRule
        {
          Path = new NodePath(rule["path"].AsString()),
          Name = rule["name"].AsStringName(),
          Index = rule["index"].AsInt32(),
          Component = component,
          Enabled = rule["enabled"].AsBool(),
          Variant = variant,
        });
      }
    }

    // The whole payload validated: commit the typed fields only now, so a failed
    // parse leaves no partial state and the next EnsureParsed retries from scratch.
    _defaultVariant = defaultVariant;
    _components.Clear();
    foreach (SysColGeneric.KeyValuePair<string, WardrobeComponent> component in components)
      _components[component.Key] = component.Value;
    _maskRules.Clear();
    _maskRules.AddRange(maskRules);
    _pieces.Clear();
    foreach (string component in components.Keys)
      _pieces.Add(new ModelClothingPiece(this, component));
    _outfits.Clear();
    for (int i = 0; i < variants.Count; i++)
      _outfits.Add(new ModelOutfit(this, variants[i], i));
  }
}
