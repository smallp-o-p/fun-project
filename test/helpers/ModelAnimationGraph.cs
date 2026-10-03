using System;
using Godot;

namespace FunProject.Tests;

/// <summary>
/// Read-only inspection of an authored AnimationNodeBlendTree graph for tests:
/// the Add2 preset layers and their weight parameters, in sorted node order so
/// discovery stays deterministic. Preset layers follow the authored convention
/// of a '&lt;Layer&gt;' Add2 weight and its '&lt;Layer&gt;Clip' animation node;
/// Add2 layers without the Clip companion (the neutral-pose 'FaceBase' base
/// layer) are not presets and stay excluded.
/// </summary>
public static class ModelAnimationGraph
{
  /// <summary>The graph's Add2 preset layer names, sorted.</summary>
  public static SysColGeneric.List<string> SortedAddNodeNames(AnimationNodeBlendTree graph)
  {
    var names = new SysColGeneric.List<string>();
    foreach (string nodeName in graph.GetNodeList())
    {
      if (graph.GetNode(new StringName(nodeName)) is AnimationNodeAdd2
        && graph.GetNode(new StringName(nodeName + "Clip")) is AnimationNodeAnimation)
        names.Add(nodeName);
    }

    names.Sort(StringComparer.Ordinal);
    return names;
  }

  /// <summary>The add_amount weight parameter of each Add2 preset layer, sorted layer order.</summary>
  public static SysColGeneric.List<string> SortedPresetParameters(AnimationNodeBlendTree graph)
  {
    var parameters = new SysColGeneric.List<string>();
    foreach (string name in SortedAddNodeNames(graph))
      parameters.Add($"parameters/{name}/add_amount");
    return parameters;
  }
}
