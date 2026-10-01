using System;
using FunProject.Models;
using Godot;

namespace FunProject.Tests;

/// <summary>
/// Owns a synthetic model tree for model tests: a CharacterModel root with a
/// Skeleton3D carrying a head bone and an AnimationPlayer. Optionally adds the
/// root to the test SceneTree (which runs the model's _Ready initialization);
/// disposal frees the tree and is idempotent. Invalid-authoring tests construct
/// the fixture unstarted and call the internal initializers directly, so
/// authoring errors never cross a Godot _Ready callback. <see cref="FromScene"/>
/// is the alternative owner for real model scenes.
/// </summary>
public sealed class ModelFixture : IDisposable
{
  private bool _disposed;
  private readonly CharacterModel? _model;
  private readonly Node _root;

  public ModelFixture(bool start = true)
  {
    // Minimal synthetic roots carry no mesh container; an empty path leaves the
    // root's appearance unconfigured (WithAppearance targets its wrapper instead).
    _model = new CharacterModel { Name = "Model", MeshRoot = new() };
    _root = _model;
    Skeleton = new Skeleton3D { Name = "Skeleton" };
    Skeleton.AddBone("head");
    Player = new AnimationPlayer { Name = "AnimationPlayer" };
    Player.AddAnimationLibrary("", new AnimationLibrary());
    Model.AddChild(Skeleton);
    Model.AddChild(Player);
    if (start)
      Start();
  }

  /// <summary>
  /// The fixture root as a CharacterModel. Throws on a scene fixture whose root is
  /// not migrated yet, so asset tests can assert on <see cref="Root"/> first.
  /// </summary>
  public CharacterModel Model => _model ?? throw new InvalidOperationException(
    $"The fixture root '{_root.Name}' is not a CharacterModel; the model scene is not migrated.");

  /// <summary>The owned scene root: the CharacterModel itself for synthetic fixtures.</summary>
  public Node Root => _root;

  /// <summary>Loads a real model scene and owns one instantiation of it; every call
  /// produces an independent instance while the native library and graph resources
  /// stay shared. Starting (the default) enters the tree, which runs the model's
  /// _Ready initialization; start:false keeps the root outside the tree for
  /// structural assertions that must not run scene scripts.</summary>
  public static ModelFixture FromScene(string scenePath, bool start = true)
  {
    PackedScene? scene = ResourceLoader.Load<PackedScene>(scenePath);
    if (scene is null)
      throw new InvalidOperationException($"Cannot load the model scene '{scenePath}'.");
    return new ModelFixture(scene.Instantiate(), start);
  }

  private ModelFixture(Node sceneRoot, bool start)
  {
    _root = sceneRoot;
    _model = sceneRoot as CharacterModel;
    if (start)
      Start();
  }

  public Skeleton3D Skeleton { get; } = null!;
  public AnimationPlayer Player { get; } = null!;

  // Present only on mask/wardrobe fixtures (see WithMask/WithWardrobe): the
  // root-relative mesh path of the masked body and the authored configuration.
  public NodePath MaskPath { get; private set; } = null!;
  public ModelMaskConfiguration? MaskConfiguration { get; private set; }
  public MeshInstance3D Body { get; private set; } = null!;
  public ArrayMesh SourceMesh { get; private set; } = null!;
  public Node3D? GarmentA { get; private set; }
  public Node3D? GarmentB { get; private set; }

  // Present only when a wardrobe fixture scaffolds the second mask pair
  // (see WithWardrobe's accessoryMaskConfiguration).
  public NodePath? AccessoryMaskPath { get; private set; }

  // Present only on appearance fixtures (see WithAppearance).
  public ShaderMaterial? SharedMaterial { get; private set; }
  public Skeleton3D? FaceSkeleton { get; private set; }

  /// <summary>The isolated outline pass of the appearance fixture's body surface.</summary>
  public ShaderMaterial Outline
  {
    get
    {
      Material? surface = Body.GetSurfaceOverrideMaterial(0);
      return surface?.NextPass as ShaderMaterial
        ?? throw new InvalidOperationException("The fixture body has no isolated outline material.");
    }
  }

  // Present only on animation fixtures (see WithAnimations).
  public AnimationTree AnimationTree { get; private set; } = null!;
  public MeshInstance3D Face { get; private set; } = null!;

  // Godot marshals native resources into fresh C# wrappers on property reads, so
  // native mesh/material identity is compared by instance id, not managed references.
  internal static bool SameNative(GodotObject? a, GodotObject? b)
    => a is not null && b is not null && a.GetInstanceId() == b.GetInstanceId();

  /// <summary>
  /// Standalone mask fixture: a "Body" MeshInstance3D carrying <paramref name="source"/>
  /// plus a mask setup on the root (root-relative "Body" mesh path) bound to it through
  /// <paramref name="configuration"/>.
  /// </summary>
  public static ModelFixture WithMask(ArrayMesh source, ModelMaskConfiguration configuration, bool start = true)
  {
    var fixture = new ModelFixture(start: false);
    fixture.SourceMesh = source;
    fixture.Body = new MeshInstance3D { Name = "Body", Mesh = source };
    fixture.Model.AddChild(fixture.Body);
    fixture.MaskPath = new NodePath("Body");
    fixture.MaskConfiguration = configuration;
    fixture.Model.MaskSetups = new Godot.Collections.Array<ModelMeshMaskSetup>
    {
      new() { MeshPath = fixture.MaskPath, Configuration = configuration },
    };
    if (start)
      fixture.Start();
    return fixture;
  }

  /// <summary>
  /// Wardrobe fixture: the mask setup plus the synthetic two-variant wardrobe
  /// configuration authored on the root, and GarmentA/GarmentB pieces (variants
  /// 0 and 1). The mask configuration carries <paramref name="maskCount"/> masks while
  /// the wardrobe rules only drive Mask0 and Mask1, so higher regions stay
  /// unrelated state. Tests of invalid authored data pass a faulty
  /// <paramref name="configuration"/> and call the root's <c>Initialize()</c>
  /// directly before tree entry. A non-null <paramref name="accessoryMaskConfiguration"/>
  /// additionally scaffolds a second "AccessoryBody" mesh and its mask setup, whose
  /// mesh path carries no component-associated rule.
  /// </summary>
  public static ModelFixture WithWardrobe(bool start = true, int maskCount = 2,
    ModelWardrobeConfiguration? configuration = null,
    ModelMaskConfiguration? accessoryMaskConfiguration = null)
  {
    ArrayMesh source = TestData.MakeMaskedSourceMesh(maskCount);
    var fixture = WithMask(source, TestData.MakeModelMaskConfiguration(source, maskCount), start: false);
    fixture.Model.WardrobeConfiguration =
      configuration ?? TestData.MakeWardrobeConfiguration();
    AddGarmentPair(fixture);
    if (accessoryMaskConfiguration is not null)
    {
      var body = new MeshInstance3D
      { Name = "AccessoryBody", Mesh = TestData.MakeMaskedSourceMesh(maskCount) };
      fixture.Model.AddChild(body);
      fixture.AccessoryMaskPath = new NodePath("AccessoryBody");
      fixture.Model.MaskSetups.Add(new ModelMeshMaskSetup
      {
        MeshPath = fixture.AccessoryMaskPath,
        Configuration = accessoryMaskConfiguration,
      });
    }

    if (start)
      fixture.Start();
    return fixture;
  }

  private static void AddGarmentPair(ModelFixture fixture)
  {
    fixture.GarmentA = new Node3D { Name = "GarmentA" };
    fixture.GarmentB = new Node3D { Name = "GarmentB" };
    fixture.Model.AddChild(fixture.GarmentA);
    fixture.Model.AddChild(fixture.GarmentB);
  }

  /// <summary>The discovered clothing piece of the wardrobe fixture, or a failure naming the gap.</summary>
  public ModelClothingPiece Piece(StringName id)
    => Model.FindPiece(id).Match(
      piece => piece,
      () => throw new InvalidOperationException($"The fixture model has no piece '{id}'."));

  /// <summary>The discovered outfit of the wardrobe fixture, or a failure naming the gap.</summary>
  public ModelOutfit Outfit(StringName id)
    => Model.FindOutfit(id).Match(
      outfit => outfit,
      () => throw new InvalidOperationException($"The fixture model has no outfit '{id}'."));

  /// <summary>
  /// Appearance fixture: a "Model" wrapper (like the real scenes' mesh container)
  /// holding a Body MeshInstance3D and FaceSkeleton. Pack/instantiate the authored
  /// subtree so Godot localizes materials exactly as it does for imported scenes.
  /// Authored settings are applied before instantiation, never at tree entry.
  /// </summary>
  public static ModelFixture WithAppearance(bool withMask = false, bool start = true,
    Action<ShaderMaterial>? authorMaterial = null, Basis? headRest = null)
  {
    var fixture = new ModelFixture(start: false);
    fixture.SourceMesh = TestData.MakeMaskedSourceMesh(2);
    // The authored outline material carries per-vertex outline weights for the mask.
    ShaderMaterial authored = TestData.MakeModelFaceMaterial();
    authorMaterial?.Invoke(authored);
    fixture.SharedMaterial = authored;
    fixture.Body = new MeshInstance3D
    { Name = "Body", Mesh = fixture.SourceMesh, Skeleton = "../FaceSkeleton" };
    fixture.Body.SetSurfaceOverrideMaterial(0, authored);
    fixture.FaceSkeleton = new Skeleton3D { Name = "FaceSkeleton" };
    fixture.FaceSkeleton.AddBone(CharacterModel.FaceBoneName);
    fixture.FaceSkeleton.SetBoneRest(0, new Transform3D(headRest ?? Basis.Identity, Vector3.Zero));
    fixture.FaceSkeleton.ResetBonePose(0);
    var wrapper = new Node3D { Name = "Model" };
    wrapper.AddChild(fixture.Body);
    wrapper.AddChild(fixture.FaceSkeleton);
    fixture.Body.Owner = wrapper;
    fixture.FaceSkeleton.Owner = wrapper;
    wrapper.SetMeta(CharacterModel.FaceLightingMetadata, new Godot.Collections.Array
    {
      new Godot.Collections.Dictionary
      {
        ["mesh_path"] = new NodePath("Body"), ["surface_index"] = 0,
        ["skeleton_path"] = new NodePath("FaceSkeleton"), ["head_bone"] = 0,
        ["inverse_rest"] = fixture.FaceSkeleton.GetBoneGlobalRest(0).Basis.Inverse(),
      },
    });
    using var scene = new PackedScene();
    if (scene.Pack(wrapper) != Error.Ok)
      throw new InvalidOperationException("Cannot pack the authored appearance fixture.");
    Node instance = scene.Instantiate();
    wrapper.Free();
    fixture.Body = instance.GetNode<MeshInstance3D>("Body");
    fixture.FaceSkeleton = instance.GetNode<Skeleton3D>("FaceSkeleton");
    fixture.Model.AddChild(instance);
    fixture.Model.MeshRoot = "Model";
    if (withMask)
    {
      fixture.MaskPath = new NodePath("Model/Body");
      fixture.MaskConfiguration = TestData.MakeModelMaskConfiguration(fixture.SourceMesh, 2);
      fixture.Model.MaskSetups = new Godot.Collections.Array<ModelMeshMaskSetup>
      {
        new() { MeshPath = fixture.MaskPath, Configuration = fixture.MaskConfiguration },
      };
    }
    if (start)
      fixture.Start();
    return fixture;
  }

  /// <summary>
  /// Animation fixture: the synthetic model plus a Face mesh carrying the Smile
  /// and Blink blend shapes, a body bone beside the head bone, and an
  /// AnimationTree in manual mode driven by the shared preset library. Preset
  /// weights are the tree's own parameters/Add2 amount parameters.
  /// </summary>
  public static ModelFixture WithAnimations(bool start = true)
  {
    var fixture = new ModelFixture(start: false);
    fixture.Face = new MeshInstance3D { Name = "Face", Mesh = TestData.MakeExpressionMesh() };
    fixture.Model.AddChild(fixture.Face);
    fixture.Skeleton.AddBone("body");
    fixture.Player.RemoveAnimationLibrary("");
    fixture.Player.AddAnimationLibrary("", TestData.MakeExpressionLibrary());
    var tree = new AnimationTree
    {
      Name = "ModelAnimationTree",
      AnimPlayer = "../AnimationPlayer",
      TreeRoot = TestData.MakeExpressionTree(),
      CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
      Active = true,
    };
    tree.Set("parameters/SmileDelta/sub_amount", 1.0);
    tree.Set("parameters/BlinkDelta/sub_amount", 1.0);
    tree.Set("parameters/SmileExtraDelta/sub_amount", 1.0);
    tree.Set("parameters/Smile/add_amount", 0.0);
    tree.Set("parameters/Blink/add_amount", 0.0);
    tree.Set("parameters/SmileExtra/add_amount", 0.0);
    fixture.Model.AddChild(tree);
    fixture.AnimationTree = tree;
    if (start)
      fixture.Start();
    return fixture;
  }

  public void Start()
  {
    if (_disposed || _root.IsInsideTree())
      return;
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(_root);
    // A manual-mode AnimationTree spends its first Advance on the initial seek,
    // applying the t=0 pose without advancing playback, so every later advance
    // evaluates one sample late. A zero-time evaluation here consumes that seek;
    // this is one-time test setup, not a per-frame writer.
    AnimationTree?.Advance(0);
  }

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    if (GodotObject.IsInstanceValid(_root))
    {
      if (_root.IsInsideTree())
        _root.GetTree().Root.RemoveChild(_root);
      _root.Free();
    }
  }
}
