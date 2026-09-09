using System;
using System.Collections.Generic;
using Godot;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>
/// Shared, read-only predicate evaluated against the supplied campaign (the
/// <see cref="FunProject.Buffs.BuffCondition"/> convention): authored configuration is
/// immutable during a campaign, instances carry no job/completion state, and evaluation
/// never caches campaign references.
/// </summary>
[GlobalClass]
public abstract partial class ResearchCondition : Resource
{
  internal abstract bool IsMet(in CampaignGameState state);

  internal void CheckAuthoring()
    => CheckAuthoring(new SysColGeneric.HashSet<ResearchCondition>(ReferenceEqualityComparer.Instance));

  private void CheckAuthoring(SysColGeneric.HashSet<ResearchCondition> path)
  {
    if (!path.Add(this))
      throw new InvalidOperationException("Research conditions contain a cycle.");
    CheckContents(path);
    path.Remove(this);
  }

  internal virtual void CheckContents(SysColGeneric.HashSet<ResearchCondition> path) { }

  internal static void CheckChild(ResearchCondition child, SysColGeneric.HashSet<ResearchCondition> path)
  {
    ArgumentNullException.ThrowIfNull(child);
    child.CheckAuthoring(path);
  }

  internal static void CheckChildren(IReadOnlyList<ResearchCondition> children,
    SysColGeneric.HashSet<ResearchCondition> path)
  {
    ArgumentNullException.ThrowIfNull(children);
    foreach (var child in children)
      CheckChild(child, path);
  }
}
