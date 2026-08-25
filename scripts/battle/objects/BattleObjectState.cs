using System;

namespace FunProject.Battle;

/// <summary>The terminal lifecycle state of a board object; a live object carries no status.</summary>
public enum ObjectStatus
{
  Interacted,
  Expired,
}

/// <summary>Mutable runtime state for one special board object.</summary>
public sealed class BattleObjectState
{
  private readonly SysColGeneric.List<SpecialObjectCapability> _capabilities = [];

  /// <summary>Stable runtime identifier for this object within one session.</summary>
  public int Id { get; }
  /// <summary>The authored template this runtime object was created from.</summary>
  public BattleSpecialObjectData Data { get; }
  /// <summary>The object's terminal status, if any: <c>None</c> while it is live on the
  /// board — presence on the board already says "placed" — and
  /// <see cref="ObjectStatus.Interacted"/> or <see cref="ObjectStatus.Expired"/> after.</summary>
  public Option<ObjectStatus> Status { get; internal set; }
  /// <summary>The object's board position; special objects never move.</summary>
  public Vector3I Position { get; }
  /// <summary>Convenience access to <see cref="BattleSpecialObjectData.Name"/>.</summary>
  public string Name => Data.Name;

  internal BattleObjectState(int id, BattleSpecialObjectData data, Vector3I position)
  {
    ArgumentNullException.ThrowIfNull(data);
    Id = id;
    Data = data;
    Position = position;
    Status = None;

    foreach (SpecialObjectCapabilityData capabilityData in data.Capabilities)
    {
      SpecialObjectCapability capability = capabilityData.CreateRuntime();
      if (_capabilities.AsValueEnumerable().Any(existing => existing.GetType().IsAssignableTo(capability.GetType())
                                     || capability.GetType().IsAssignableTo(existing.GetType())))
        throw new InvalidOperationException($"Object '{Name}' has more than one {capability.GetType().Name}.");
      _capabilities.Add(capability);
    }
  }

  /// <summary>Finds the object's runtime capability of the requested type, if present.</summary>
  public Option<TCap> FindCapability<TCap>() where TCap : SpecialObjectCapability
  {
    foreach (var capability in _capabilities.AsValueEnumerable().OfType<TCap>())
      return capability;
    return None;
  }
}
