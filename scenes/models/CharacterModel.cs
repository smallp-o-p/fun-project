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

  public override void _Ready()
  {
    // Face axes must be sampled after the native AnimationTree applies its pose.
    // ProcessPriority orders the mixer's internal process (idle mode, priority 0)
    // and this root's regular _Process together, lower first, so without
    // priority 1 tree order alone would run this root first and the shader
    // uniforms would lag the mixer by a frame.
    ProcessPriority = 1;
    Initialize();
  }

  public override void _Process(double delta) => UpdateFaceAxes();

  private const string AnimationTreeChildName = "ModelAnimationTree";

  /// <summary>
  /// First-time initialization: resolve the animation tree child and the
  /// attachments path, bind the imported face-lighting slots, then initialize
  /// the mask setups, the wardrobe, and the attachments. A missing <c>ModelAnimationTree</c>
  /// child leaves that component unconfigured; a wrong-type child is an authoring
  /// error, and any failure fails initialization, so a repeated Initialize (or
  /// the <see cref="MaskSetups"/> late-assignment retry) retries it.
  /// </summary>
  internal void Initialize()
  {
    if (_initialized)
      return;
    _animationTree = ResolveAnimationTree();
    _attachments = ResolveAttachmentContainer();
    InitializeAppearance();
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

  private Godot.AnimationTree? ResolveAnimationTree()
  {
    if (GetNodeOrNull(AnimationTreeChildName) is not { } child)
      return null;
    if (child is not Godot.AnimationTree tree)
      throw new InvalidOperationException(
        $"The character model '{Name}' animation tree child '{AnimationTreeChildName}' is not a AnimationTree.");
    return tree;
  }

  // An empty AttachmentsPath means the model has no attachment set; a nonempty
  // path that misses or mis-types its container is an authoring error.
  private Node3D? ResolveAttachmentContainer()
  {
    if (AttachmentsPath.IsEmpty)
      return null;
    if (GetNodeOrNull(AttachmentsPath) is not Node3D container)
      throw new InvalidOperationException(
        $"The character model '{Name}' attachment container path '{AttachmentsPath}' does not resolve to a Node3D.");
    return container;
  }

  private void ApplyAttachmentsVisibility()
  {
    // The container resolves once at initialization and is never re-resolved.
    if (_attachments is not null)
      _attachments.Visible = _attachmentsVisible;
  }
}
