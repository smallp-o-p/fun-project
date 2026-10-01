#nullable disable warnings
using System;
using System.Threading.Tasks;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class CharacterModelAnimationAssetTest
{
  private const string ZhuYuanScene = "res://scenes/models/ZhuYuan/ZhuYuan.scn";
  private const string TriggerScene = "res://scenes/models/Trigger/Trigger.scn";
  private const string PlayerNode = "ModelAnimationPlayer";
  private const double FirstWeight = 0.5;
  private const double SecondWeight = 0.25;
  private const double ValueEpsilon = 1e-3;
  private const int OrderingBoundFrames = 240;
  private const int RequiredPoseChanges = 3;

  // One preset contribution discovered from the authored graph: its Add2 weight
  // parameter plus the facial target and neutral-relative endpoint pair its clip
  // drives.
  private sealed record PresetTarget(
    string Parameter, string MeshPath, string Shape, double Neutral, double Endpoint);

  // ------------------------------------------------------------------
  // Face-lighting integration: after the native mixer rotated the head via
  // the HeadTurn branch and one frame elapsed, the isolated face material's
  // world axes must match the live head-bone basis — the appearance's
  // _Process runs on its own, no test calls UpdateFaceAxes directly.
  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene)]
  [TestCase(TriggerScene)]
  public async Task FaceAxesFollowTheNativeHeadPose(string scene)
  {
    using var fixture = ModelFixture.FromScene(scene);
    var tree = fixture.Model.AnimationTree;
    tree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
    tree.Active = true;
    tree.Advance(0);
    tree.Set("parameters/HeadTurn/blend_amount", 1.0);
    tree.Advance(0.25);

    var skeleton = fixture.Root.GetNode<Skeleton3D>("Model/rig_D/GeneralSkeleton");
    int head = RequireBone(skeleton, "Head", scene);
    // process_frame emits before the frame's node _Process callbacks run, so the
    // first await resumes the test before the model root sampled the new pose;
    // the second await guarantees one complete sample pass has happened.
    await fixture.Model.GetTree().ToSignal(
      fixture.Model.GetTree(), SceneTree.SignalName.ProcessFrame);
    await fixture.Model.GetTree().ToSignal(
      fixture.Model.GetTree(), SceneTree.SignalName.ProcessFrame);

    (MeshInstance3D faceMesh, int faceSurface) = FindFaceSurface(fixture.Root);
    var face = (ShaderMaterial)faceMesh.GetSurfaceOverrideMaterial(faceSurface)!;
    Basis axes = skeleton.GlobalBasis
      * skeleton.GetBoneGlobalPose(head).Basis
      * skeleton.GetBoneGlobalRest(head).Basis.Inverse();
    Vector3 forward = (axes * Vector3.Back).Normalized();
    Vector3 right = (axes * Vector3.Right).Normalized();
    Assert.True(face.GetShaderParameter(CharacterModel.HeadForwardParameter).AsVector3()
        .DistanceTo(forward) < 1e-3,
      $"The '{scene}' face material's head_forward_world did not follow the native head pose.");
    Assert.True(face.GetShaderParameter(CharacterModel.HeadRightParameter).AsVector3()
        .DistanceTo(right) < 1e-3,
      $"The '{scene}' face material's head_right_world did not follow the native head pose.");
  }

  // ------------------------------------------------------------------
  // Automatic ordering coverage: a test-owned looping clip drives the tracked
  // face bone through an automatically processed, continuously changing head
  // pose (default idle callback mode — no manual Advance). The model root sits
  // earlier in tree order than its animation tree child, so equal priority
  // would run its _Process first and sample the stale pose; priority 1
  // schedules it after the mixer's priority-0 internal process
  // (process_priority orders NOTIFICATION_PROCESS and
  // NOTIFICATION_INTERNAL_PROCESS together, lower first). Each await resumes
  // at the next process_frame — before that frame's node processing — so the
  // sampled state is the previous completed frame's; whenever the pose changed
  // across that boundary, the uniforms must already match this frame's pose.
  // ------------------------------------------------------------------

  [TestCase]
  public async Task AutomaticHeadMotionUpdatesFaceAxesAtCompletedFrames()
  {
    using var fixture = ModelFixture.WithAppearance(start: false);
    fixture.Player.GetAnimationLibrary("").AddAnimation("head_turn_loop", MakeLoopingHeadTurnClip());
    var tree = new AnimationTree
    {
      Name = "AnimationTree",
      AnimPlayer = "../AnimationPlayer",
      TreeRoot = new AnimationNodeAnimation { Animation = "head_turn_loop" },
      CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Idle,
      Active = true,
    };
    fixture.Model.AddChild(tree);
    fixture.Start();

    var skeleton = fixture.FaceSkeleton!;
    int head = skeleton.FindBone(CharacterModel.FaceBoneName);
    var face = (ShaderMaterial)fixture.Body.GetSurfaceOverrideMaterial(0)!;
    var sceneTree = fixture.Model.GetTree();

    Quaternion previousPose = skeleton.GetBoneGlobalPose(head).Basis.GetRotationQuaternion();
    int verifiedChanges = 0;
    for (int frame = 0; frame < OrderingBoundFrames && verifiedChanges < RequiredPoseChanges; frame++)
    {
      await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
      Quaternion pose = skeleton.GetBoneGlobalPose(head).Basis.GetRotationQuaternion();
      if (pose.IsEqualApprox(previousPose))
        continue;
      Basis axes = skeleton.GlobalBasis
        * skeleton.GetBoneGlobalPose(head).Basis
        * skeleton.GetBoneGlobalRest(head).Basis.Inverse();
      Vector3 expectedForward = (axes * Vector3.Back).Normalized();
      Vector3 expectedRight = (axes * Vector3.Right).Normalized();
      Vector3 uniformForward = face.GetShaderParameter(CharacterModel.HeadForwardParameter).AsVector3();
      Vector3 uniformRight = face.GetShaderParameter(CharacterModel.HeadRightParameter).AsVector3();
      Assert.True(uniformForward.DistanceTo(expectedForward) < 1e-3,
        $"The face uniforms lagged the automatically applied head pose at a completed-frame boundary: " +
        $"forward {uniformForward} instead of {expectedForward}.");
      Assert.True(uniformRight.DistanceTo(expectedRight) < 1e-3,
        $"The face uniforms lagged the automatically applied head pose at a completed-frame boundary: " +
        $"right {uniformRight} instead of {expectedRight}.");
      verifiedChanges++;
      previousPose = pose;
    }

    Assert.True(verifiedChanges >= RequiredPoseChanges,
      $"The tracked head pose never changed in {OrderingBoundFrames} awaited frames; " +
      "the automatic mixer did not drive the face bone.");
  }

  // A test-owned looping clip rotating the tracked face bone, so consecutive
  // completed frames always sample a different head pose.
  private static Animation MakeLoopingHeadTurnClip()
  {
    var clip = new Animation { Length = 1.0, LoopMode = Animation.LoopModeEnum.Linear };
    int track = clip.AddTrack(Animation.TrackType.Rotation3D);
    clip.TrackSetPath(track, $"Model/FaceSkeleton:{CharacterModel.FaceBoneName}");
    clip.TrackInsertKey(track, 0.0, Quaternion.Identity);
    clip.TrackInsertKey(track, 1.0, Quaternion.FromEuler(new Vector3(0, MathF.PI / 4, 0)));
    return clip;
  }

  // ------------------------------------------------------------------
  // Clothing with the graph active: a wardrobe derivation must neither
  // reset the preset weights nor stop the native playback afterwards.
  // ------------------------------------------------------------------

  [TestCase(TriggerScene)]
  public void ClothingChangeKeepsPresetWeightsAndPlayback(string scene)
  {
    using var fixture = ModelFixture.FromScene(scene);
    var library = fixture.Root.GetNode<AnimationPlayer>(PlayerNode).GetAnimationLibrary("body");
    var tree = fixture.Model.AnimationTree;
    string presetParameter = ModelAnimationGraph.SortedPresetParameters(
      (AnimationNodeBlendTree)tree.TreeRoot)[0];
    (string motionNode, string motionBone) = MotionTrackFromClip(library.GetAnimation("motion_demo"));

    tree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
    tree.Active = true;
    tree.Advance(0);
    tree.Set(presetParameter, 0.5);
    tree.Set("parameters/Movement/blend_amount", 1.0);

    var wardrobe = fixture.Model;
    wardrobe.ClothingEnabled = false;
    wardrobe.ClothingEnabled = true;

    var motionSkeleton = fixture.Root.GetNode<Skeleton3D>(motionNode);
    int motionIndex = RequireBone(motionSkeleton, motionBone, scene);
    tree.Advance(0.25);
    Quaternion first = motionSkeleton.GetBonePoseRotation(motionIndex);
    Assert.Equal(0.5, tree.Get(presetParameter).AsDouble(),
      $"The clothing change on '{scene}' reset the preset weight '{presetParameter}'.");
    tree.Advance(0.25);
    Quaternion second = motionSkeleton.GetBonePoseRotation(motionIndex);
    Assert.False(first.IsEqualApprox(second),
      $"The motion bone '{motionBone}' did not keep advancing after the clothing change on '{scene}'.");
  }

  // ------------------------------------------------------------------
  // Physical verification on the authored native track paths. Every
  // target and expected value is derived from the player's clips and
  // the graph's Add2 preset layers: the motion_demo rotation track
  // whose keys differ, the head_turn rotation endpoint, and two preset
  // contributions with disjoint movable facial targets. The graph must
  // move the motion bone between elapsed 0.25 and 0.5, hold the head at
  // the authored endpoint at both samples, drive both presets to their
  // neutral-relative weighted endpoints, and restore each target to its
  // own neutral when its weight returns to zero — in both directions,
  // so zeroing either preset while the other stays weighted moves only
  // its own target.
  // ------------------------------------------------------------------

  [TestCase(ZhuYuanScene)]
  [TestCase(TriggerScene)]
  public void GraphDrivesPhysicalBoneAndFaceTargets(string scene)
  {
    using var fixture = ModelFixture.FromScene(scene);
    var player = fixture.Root.GetNode<AnimationPlayer>(PlayerNode);
    var library = player.GetAnimationLibrary("body");
    var tree = fixture.Model.AnimationTree;
    var graph = (AnimationNodeBlendTree)tree.TreeRoot;

    (string motionNode, string motionBone) = MotionTrackFromClip(library.GetAnimation("motion_demo"));
    (string headNode, string headBone, Quaternion headEndpoint) = HeadTurnTrackFromClip(library.GetAnimation("head_turn"));
    (PresetTarget first, PresetTarget second) = DisjointPresetTargets(player, graph, scene);

    tree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
    tree.Active = true;
    tree.Advance(0); // Prime the initial seek before measuring elapsed playback.
    tree.Set(first.Parameter, FirstWeight);
    tree.Set(second.Parameter, SecondWeight);
    tree.Set("parameters/Movement/blend_amount", 1.0);
    tree.Set("parameters/HeadTurn/blend_amount", 1.0);

    var motionSkeleton = fixture.Root.GetNode<Skeleton3D>(motionNode);
    var headSkeleton = fixture.Root.GetNode<Skeleton3D>(headNode);
    int motionIndex = RequireBone(motionSkeleton, motionBone, scene);
    int headIndex = RequireBone(headSkeleton, headBone, scene);
    var firstMesh = fixture.Root.GetNode<MeshInstance3D>(first.MeshPath);
    var secondMesh = fixture.Root.GetNode<MeshInstance3D>(second.MeshPath);
    int firstShapeIndex = RequireBlendShape(firstMesh, first.Shape, scene);
    int secondShapeIndex = RequireBlendShape(secondMesh, second.Shape, scene);

    tree.Advance(0.25);
    Quaternion motionAtQuarter = motionSkeleton.GetBonePoseRotation(motionIndex);
    Quaternion headAtQuarter = headSkeleton.GetBonePoseRotation(headIndex);
    float firstAtQuarter = firstMesh.GetBlendShapeValue(firstShapeIndex);
    float secondAtQuarter = secondMesh.GetBlendShapeValue(secondShapeIndex);

    AssertHeadHoldsEndpoint(scene, headBone, headAtQuarter, headEndpoint, "elapsed 0.25");
    AssertWeightedValue(scene, first, firstAtQuarter, FirstWeight);
    AssertWeightedValue(scene, second, secondAtQuarter, SecondWeight);

    tree.Advance(0.25);
    Quaternion motionAtHalf = motionSkeleton.GetBonePoseRotation(motionIndex);
    Quaternion headAtHalf = headSkeleton.GetBonePoseRotation(headIndex);
    Assert.False(motionAtQuarter.IsEqualApprox(motionAtHalf),
      $"The motion bone '{motionBone}' did not keep moving between elapsed 0.25 and 0.5 on '{scene}'.");
    AssertHeadHoldsEndpoint(scene, headBone, headAtHalf, headEndpoint, "elapsed 0.5");

    tree.Set(first.Parameter, 0.0);
    tree.Advance(0);
    Assert.True(Math.Abs(firstMesh.GetBlendShapeValue(firstShapeIndex) - first.Neutral) <= ValueEpsilon,
      $"Zeroing '{first.Parameter}' left '{first.Shape}' at {firstMesh.GetBlendShapeValue(firstShapeIndex)} instead of its neutral {first.Neutral} on '{scene}'.");
    Assert.True(Math.Abs(secondMesh.GetBlendShapeValue(secondShapeIndex)
      - (second.Neutral + SecondWeight * (second.Endpoint - second.Neutral))) <= ValueEpsilon,
      $"Zeroing '{first.Parameter}' also moved the independent '{second.Shape}' on '{scene}'.");

    // Reverse direction: restoring the first weight while zeroing the second
    // must return the first to its exact weighted value and the second to its
    // own neutral, so neither channel depends on the other's weight.
    tree.Set(first.Parameter, FirstWeight);
    tree.Set(second.Parameter, 0.0);
    tree.Advance(0);
    AssertWeightedValue(scene, first, firstMesh.GetBlendShapeValue(firstShapeIndex), FirstWeight);
    Assert.True(Math.Abs(secondMesh.GetBlendShapeValue(secondShapeIndex) - second.Neutral) <= ValueEpsilon,
      $"Zeroing '{second.Parameter}' left '{second.Shape}' at {secondMesh.GetBlendShapeValue(secondShapeIndex)} instead of its neutral {second.Neutral} on '{scene}'.");
  }

  // A clip may drive several blend-shape channels per the authoring guide, so
  // selection must enumerate every track: the extended Smile clip conflicts
  // with both the Blink and the SmileExtra preset, and the enabled Blink filter
  // path makes that interference real in the graph, leaving Blink and
  // SmileExtra as the only genuinely independent pair.
  [TestCase]
  public void TargetSelectionHandlesMultiTrackClips()
  {
    using var fixture = ModelFixture.WithAnimations(start: false);
    Animation smile = fixture.Player.GetAnimationLibrary("").GetAnimation("smile");
    SetConstantEndpoint(smile, "Face:Blink", 0.6f);
    var graph = (AnimationNodeBlendTree)fixture.AnimationTree.TreeRoot;
    var smileAdd = graph.GetNode(new StringName("Smile")) as AnimationNodeAdd2
      ?? throw new InvalidOperationException("The fixture graph has no 'Smile' Add2 preset node.");
    smileAdd.SetFilterPath("Face:Blink", true);
    fixture.Start();

    (PresetTarget first, PresetTarget second) = DisjointPresetTargets(
      fixture.Player, graph, "the multi-track fixture");

    Assert.Equal("parameters/Blink/add_amount", first.Parameter,
      $"The multi-track fixture selected '{first.Parameter}' first; its Blink channel is free of SmileExtra contributions, while the Smile preset's clip also drives Blink and conflicts with it.");
    Assert.Equal("Blink", first.Shape,
      $"The multi-track fixture selected channel '{first.Shape}' first.");
    Assert.Equal("parameters/SmileExtra/add_amount", second.Parameter,
      $"The multi-track fixture selected '{second.Parameter}' second.");
    Assert.Equal("Smile", second.Shape,
      $"The multi-track fixture selected channel '{second.Shape}' second.");
  }

  // The fixture's smile clip edited to carry a negative Smile endpoint (below
  // its 0.2 neutral) and a positive Blink endpoint, with the Smile Add2 filter
  // passing both channels: one preset weight then applies both neutral-relative
  // deltas at once (Smile −0.1, Blink +0.3).
  [TestCase]
  public void MultiChannelPresetCarriesNegativeAndPositiveDeltas()
  {
    using var fixture = ModelFixture.WithAnimations(start: false);
    Animation smile = fixture.Player.GetAnimationLibrary("").GetAnimation("smile");
    SetConstantEndpoint(smile, "Face:Smile", 0.0f);
    SetConstantEndpoint(smile, "Face:Blink", 0.6f);
    var graph = (AnimationNodeBlendTree)fixture.AnimationTree.TreeRoot;
    var smileAdd = graph.GetNode(new StringName("Smile")) as AnimationNodeAdd2
      ?? throw new InvalidOperationException("The fixture graph has no 'Smile' Add2 preset node.");
    smileAdd.SetFilterPath("Face:Blink", true);
    fixture.Start();

    AnimationTree tree = fixture.AnimationTree;
    Variant live = tree.Get("parameters/SmileDelta/sub_amount");
    Assert.True(live.VariantType is Variant.Type.Float or Variant.Type.Int,
      $"The fixture tree has no numeric live parameter 'SmileDelta/sub_amount' (actual Variant type {live.VariantType}); a missing parameter must fail instead of coercing to the expectation.");
    Assert.Equal(1.0, live.AsDouble(),
      $"The fixture-initialized subtraction amount is {live.AsDouble()} instead of the authored 1.");

    int smileIndex = fixture.Face.FindBlendShapeByName("Smile");
    int blinkIndex = fixture.Face.FindBlendShapeByName("Blink");
    tree.Set("parameters/Smile/add_amount", 0.5);
    tree.Advance(0);
    Assert.True(Math.Abs(fixture.Face.GetBlendShapeValue(smileIndex) - 0.1) <= ValueEpsilon,
      $"The negative smile delta read {fixture.Face.GetBlendShapeValue(smileIndex)} instead of the neutral-relative −0.1 at weight 0.5.");
    Assert.True(Math.Abs(fixture.Face.GetBlendShapeValue(blinkIndex) - 0.3) <= ValueEpsilon,
      $"The positive blink delta read {fixture.Face.GetBlendShapeValue(blinkIndex)} instead of the neutral-relative 0.3 at weight 0.5.");
  }

  // ------------------------------------------------------------------
  // Synthetic lifecycle: a parameter written before the root's _Ready must
  // survive initialization, and its physical output proves it: overlapping
  // same-channel presets (Smile and SmileExtra) at full weight both add
  // their neutral-relative deltas onto the Smile channel, summing to
  // 0.2 + (0.8 − 0.2) + (0.5 − 0.2) = 1.1 with no normalization or clamping.
  // Real scenes author the zero defaults, so they cannot distinguish a reset
  // from a preserved authored value.
  // ------------------------------------------------------------------

  [TestCase]
  public void InitializationPreservesAuthoredParameters()
  {
    using var fixture = ModelFixture.WithAnimations(start: false);
    fixture.AnimationTree.Set("parameters/Smile/add_amount", 1.0);
    fixture.AnimationTree.Set("parameters/SmileExtra/add_amount", 1.0);
    fixture.Start();
    Assert.Equal(1.0, fixture.AnimationTree.Get("parameters/Smile/add_amount").AsDouble());
    Assert.Equal(1.0, fixture.AnimationTree.Get("parameters/SmileExtra/add_amount").AsDouble());
    int smile = fixture.Face.FindBlendShapeByName("Smile");
    double output = fixture.Face.GetBlendShapeValue(smile);
    Assert.True(Math.Abs(output - 1.1) <= ValueEpsilon,
      $"The overlapping Smile/SmileExtra presets read {output} instead of the summed 1.1 without normalization or clamping.");
  }

  // Derives the moving skeletal target from motion_demo: the rotation track
  // whose authored keys actually differ (the demonstration keys one bone from
  // its baseline to a five-degree offset and back over one second).
  private static (string NodePath, string Bone) MotionTrackFromClip(Animation clip)
  {
    for (int track = 0; track < clip.GetTrackCount(); track++)
    {
      if (clip.TrackGetType(track) != Animation.TrackType.Rotation3D || clip.TrackGetKeyCount(track) < 2)
        continue;
      Quaternion atStart = clip.RotationTrackInterpolate(track, 0.0);
      Quaternion atMiddle = clip.RotationTrackInterpolate(track, clip.Length * 0.5);
      if (atStart.IsEqualApprox(atMiddle))
        continue;
      (string nodePath, string bone) = SplitTrackPath(clip.TrackGetPath(track));
      return (nodePath, bone);
    }
    throw new InvalidOperationException(
      $"'{clip.ResourcePath}' has no rotation track with differing keys; the motion demonstration is not authored.");
  }

  // Derives the head-turn target and its authored endpoint from head_turn's
  // rotation track, so the assertion compares the posed bone against the clip
  // itself rather than against any hard-coded rig assumption.
  private static (string NodePath, string Bone, Quaternion Endpoint) HeadTurnTrackFromClip(Animation clip)
  {
    for (int track = 0; track < clip.GetTrackCount(); track++)
    {
      if (clip.TrackGetType(track) != Animation.TrackType.Rotation3D)
        continue;
      (string nodePath, string bone) = SplitTrackPath(clip.TrackGetPath(track));
      return (nodePath, bone, clip.RotationTrackInterpolate(track, 0.0));
    }
    throw new InvalidOperationException(
      $"'{clip.ResourcePath}' has no rotation track; the head-turn endpoint is not authored.");
  }

  // Finds the sole face SDF surface whose material carries the face-axis uniforms.
  private static (MeshInstance3D Mesh, int Surface) FindFaceSurface(Node root)
  {
    foreach (Node child in root.GetChildren())
    {
      if (child is MeshInstance3D mesh && mesh.Mesh is not null)
      {
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
          if (mesh.GetSurfaceOverrideMaterial(surface) is ShaderMaterial material
            && material.GetShaderParameter(CharacterModel.UseFaceSdfParameter).AsBool())
            return (mesh, surface);
        }
      }

      (MeshInstance3D, int) descendant = FindFaceSurface(child);
      if (descendant.Item1 is not null)
        return descendant;
    }

    return (null, -1);
  }

  // Picks the first two Add2 preset layers (sorted node order) whose selected
  // facial channels are genuinely independent: a clip may drive several
  // blend-shape channels, so a pair only counts when the channel chosen from
  // each preset receives no movable contribution from the other preset's clip.
  // Each channel's endpoint must differ from the neutral clip's value so the
  // wiring and restoration assertions are actually falsifiable.
  private static (PresetTarget First, PresetTarget Second) DisjointPresetTargets(
    AnimationPlayer player, AnimationNodeBlendTree graph, string scene)
  {
    var candidates = new SysColGeneric.List<SysColGeneric.List<PresetTarget>>();
    foreach (string nodeName in ModelAnimationGraph.SortedAddNodeNames(graph))
    {
      var clipNode = graph.GetNode(new StringName(nodeName + "Clip")) as AnimationNodeAnimation
        ?? throw new InvalidOperationException(
          $"The graph has no '<{nodeName}>Clip' animation node for the preset layer '{nodeName}'.");
      // Production graphs name the preset's neutral reference '<Layer>Ref'; the
      // synthetic fixture spells the same node '<Layer>Neutral'.
      var refNode = graph.GetNode(new StringName(nodeName + "Ref")) as AnimationNodeAnimation
        ?? graph.GetNode(new StringName(nodeName + "Neutral")) as AnimationNodeAnimation;
      if (refNode is null)
        throw new InvalidOperationException(
          $"The graph has no '<{nodeName}>Ref' animation node for the preset layer '{nodeName}'.");
      Animation clip = player.GetAnimation(clipNode.Animation);
      if (clip is null)
        throw new InvalidOperationException(
          $"The preset layer '{nodeName}' clip '{clipNode.Animation}' does not exist in the player.");
      Animation neutral = player.GetAnimation(refNode.Animation);
      if (neutral is null)
        throw new InvalidOperationException(
          $"The preset layer '{nodeName}' neutral clip '{refNode.Animation}' does not exist in the player.");
      SysColGeneric.List<PresetTarget> channels = MovableChannels(
        neutral, clip, clipNode.Animation, nodeName);
      if (channels.Count > 0)
        candidates.Add(channels);
    }

    for (int i = 0; i < candidates.Count; i++)
    {
      for (int j = i + 1; j < candidates.Count; j++)
      {
        foreach (PresetTarget first in candidates[i])
        {
          if (Drives(candidates[j], first))
            continue;
          foreach (PresetTarget second in candidates[j])
          {
            if (!Drives(candidates[i], second))
              return (first, second);
          }
        }
      }
    }
    throw new InvalidOperationException(
      $"The '{scene}' graph has no two preset layers with independent movable facial targets.");
  }

  // Every blend-shape channel the clip moves away from its neutral value, in
  // track order; a preset clip may carry several channels.
  private static SysColGeneric.List<PresetTarget> MovableChannels(
    Animation neutral, Animation clip, string clipName, string nodeName)
  {
    var channels = new SysColGeneric.List<PresetTarget>();
    for (int track = 0; track < clip.GetTrackCount(); track++)
    {
      if (clip.TrackGetType(track) != Animation.TrackType.BlendShape)
        continue;
      (string meshPath, string shape) = SplitTrackPath(clip.TrackGetPath(track));
      double endpoint = clip.BlendShapeTrackInterpolate(track, 0.0);
      double neutralValue = NeutralValueOf(neutral, meshPath, shape, clipName);
      if (Math.Abs(endpoint - neutralValue) <= ValueEpsilon)
        continue;
      channels.Add(new PresetTarget($"parameters/{nodeName}/add_amount", meshPath, shape, neutralValue, endpoint));
    }
    return channels;
  }

  // True when the other preset's movable channels include this channel, i.e.
  // its clip moves the same mesh blend shape away from neutral.
  private static bool Drives(SysColGeneric.List<PresetTarget> others, PresetTarget channel)
  {
    foreach (PresetTarget other in others)
    {
      if (other.MeshPath == channel.MeshPath && other.Shape == channel.Shape)
        return true;
    }
    return false;
  }

  private static double NeutralValueOf(Animation neutral, string meshPath, string shape, string clipName)
  {
    string fullTrack = $"{meshPath}:{shape}";
    for (int track = 0; track < neutral.GetTrackCount(); track++)
    {
      if (neutral.TrackGetType(track) == Animation.TrackType.BlendShape
          && neutral.TrackGetPath(track).ToString() == fullTrack)
        return neutral.BlendShapeTrackInterpolate(track, 0.0);
    }
    throw new InvalidOperationException(
      $"The 'neutral' clip has no blend-shape track '{fullTrack}' for '{clipName}'.");
  }

  // Replaces the clip's constant endpoint on one blend-shape channel: removes
  // any existing track for the path and authors a fresh constant one, so the
  // fixture-owned edits stay deterministic.
  private static void SetConstantEndpoint(Animation clip, string trackPath, float value)
  {
    for (int track = clip.GetTrackCount() - 1; track >= 0; track--)
    {
      if (clip.TrackGetType(track) == Animation.TrackType.BlendShape
          && clip.TrackGetPath(track).ToString() == trackPath)
        clip.RemoveTrack(track);
    }
    int added = clip.AddTrack(Animation.TrackType.BlendShape);
    clip.TrackSetPath(added, trackPath);
    clip.BlendShapeTrackInsertKey(added, 0.0, value);
  }

  private static (string NodePath, string SubName) SplitTrackPath(NodePath path)
  {
    string text = path.ToString();
    int separator = text.LastIndexOf(':');
    if (separator <= 0)
      throw new InvalidOperationException($"The animation track path '{text}' has no ':target' separator.");
    return (text[..separator], text[(separator + 1)..]);
  }

  private static int RequireBone(Skeleton3D skeleton, string bone, string scene)
  {
    int index = skeleton.FindBone(bone);
    Assert.True(index >= 0, $"The '{scene}' rig '{skeleton.Name}' has no bone '{bone}'.");
    return index;
  }

  private static int RequireBlendShape(MeshInstance3D mesh, string shape, string scene)
  {
    int index = mesh.FindBlendShapeByName(shape);
    Assert.True(index >= 0, $"The '{scene}' mesh '{mesh.Name}' has no blend shape '{shape}'.");
    return index;
  }

  private static void AssertHeadHoldsEndpoint(string scene, string bone, Quaternion actual, Quaternion endpoint, string at)
  {
    Assert.True(actual.AngleTo(endpoint) <= ValueEpsilon,
      $"The head bone '{bone}' pose at {at} on '{scene}' is {actual} instead of the authored head_turn endpoint {endpoint}.");
  }

  private static void AssertWeightedValue(string scene, PresetTarget target, double actual, double weight)
  {
    double expected = target.Neutral + weight * (target.Endpoint - target.Neutral);
    Assert.True(Math.Abs(actual - expected) <= ValueEpsilon,
      $"The preset '{target.Parameter}' produced {actual} on '{target.Shape}' at weight {weight} instead of the native neutral-relative endpoint {expected} on '{scene}'.");
  }
}
