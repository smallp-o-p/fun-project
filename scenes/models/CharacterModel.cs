using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Model root node: the single C# entry point of the Zhu Yuan and Trigger model
/// scenes. Body pose, movement, and facial expression presets belong to the
/// native <c>ModelAnimationTree</c> child, resolved once on ready and published
/// for the instance's lifetime.
/// </summary>
/// <remarks>
/// Mask state is runtime-derived and never serialized; <see cref="Selections"/>
/// is the serialized clothing control surface. The <see cref="AnimationTree"/>
/// getter throws while the model is uninitialized.
/// </remarks>
[Tool]
[GlobalClass]
public partial class CharacterModel : Node3D
{
  [Export] public NodePath AttachmentsPath { get; set; } = new();

  private Node3D? _attachments;
  private bool _attachmentsVisible = true;

  /// <summary>
  /// Whole-attachment-set visibility: only the resolved container's own visible
  /// flag changes, never its children. Assignments before initialization are
  /// stored until ready; initialized assignments apply immediately when a
  /// container resolved at initialization, and store only without one.
  /// </summary>
  [Export]
  public bool AttachmentsVisible
  {
    get => _attachmentsVisible;
    set
    {
      _attachmentsVisible = value;
      if (_initialized)
        ApplyAttachmentsVisibility();
    }
  }

  private Godot.AnimationTree? _animationTree;
  private bool _initialized;

  public override void _Ready() => Initialize();

  private const string AnimationTreeChildName = "ModelAnimationTree";

  /// <summary>
  /// First-time initialization: resolve the animation tree child and the
  /// attachments path, then initialize
  /// the imported masks, wardrobe, and attachments. A missing
  /// <c>ModelAnimationTree</c> child leaves that component unconfigured.
  /// </summary>
  internal void Initialize()
  {
    if (_initialized)
      return;
    // Pre-ready selections may precede attachment of the imported child.
    // Only an absent-source placeholder is discarded, never imported controls.
    if (_catalogProvisional)
    {
      _boundWardrobe = null;
      _catalogProvisional = false;
    }
    _animationTree = ResolveAnimationTree();
    _attachments = ResolveAttachmentContainer();
    InitializeMasks();
    InitializeWardrobe();
    ApplyAttachmentsVisibility();
    _initialized = true;
  }

  public Godot.AnimationTree AnimationTree
    => _initialized && _animationTree is not null
    ? _animationTree
    : throw new InvalidOperationException(
      "The character model is not initialized; the animation tree becomes available "
      + "once it enters the scene tree and resolves its ModelAnimationTree child.");

  // Native wrapper children are bound once. Optional absence is meaningful;
  // typed GetNode performs binding without a second authoring validation layer.
  private Godot.AnimationTree? ResolveAnimationTree()
    => (Godot.AnimationTree?)GetNodeOrNull(AnimationTreeChildName);

  private Node3D? ResolveAttachmentContainer()
    => AttachmentsPath.IsEmpty ? null : GetNode<Node3D>(AttachmentsPath);

  private void ApplyAttachmentsVisibility()
  {
    // The container resolves once at initialization and is never re-resolved.
    if (_attachments is not null)
      _attachments.Visible = _attachmentsVisible;
  }
}
