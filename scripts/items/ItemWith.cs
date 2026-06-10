using FunProject.Items.Capabilities;

namespace FunProject.Items;

/// <summary>
/// Proof that an item carries a given capability — obtainable only via
/// EquippableItem.With&lt;TCap&gt;(). Cannot go stale: capability sets are
/// fixed at item construction. Same pattern as BattleBoardState.ValidatedPoint.
/// </summary>
public readonly struct ItemWith<TCap> where TCap : ItemCapability
{
  public EquippableItem Item { get; }
  public TCap Capability { get; }

  internal ItemWith(EquippableItem item, TCap capability)
  {
    Item = item;
    Capability = capability;
  }
}
