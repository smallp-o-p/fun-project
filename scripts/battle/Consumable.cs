using System;
using FunProject.Items;
using FunProject.Items.Capabilities;

namespace FunProject.Battle;

/// <summary>
/// Receipt that one use consumes this item: either ticks its charge (the item leaves the
/// owner's inventory when the last charge goes) or removes a charge-less item outright.
/// Minted from capability facts that are fixed at item construction, so it cannot go stale.
/// Same pattern as <see cref="AliveUnit"/> and <see cref="ItemWith{TCap}"/>.
/// </summary>
public sealed class Consumable
{
  public EquippableItem Item { get; }
  private readonly Option<ChargesCapability> _charges;

  private Consumable(EquippableItem item, Option<ChargesCapability> charges)
  {
    Item = item;
    _charges = charges;
  }

  // A throwable only consumes when its capability says so; a non-consuming throwable can be
  // thrown freely without ever leaving the inventory.
  public static Option<Consumable> From(ItemWith<ThrowableCapability> throwable)
    => throwable.Capability.ConsumesOnUse
      ? new Consumable(throwable.Item, throwable.Item.FindCapability<ChargesCapability>())
      : None;

  // An explicitly charged item always spends a charge per use.
  public static Consumable From(ItemWith<ChargesCapability> charged)
    => new(charged.Item, charged.Capability);

  // Trusted-core spend: the caller has proven the use is legal, so a charge that cannot be
  // spent is a broken invariant rather than a rejectable outcome.
  public void SpendOnce(BattleUnitState owner)
  {
    _charges.Match(
      charges =>
      {
        if (!charges.TrySpend())
          throw new InvalidOperationException($"{Item.ItemName} could not spend a charge.");

        if (charges.IsDepleted)
          owner.RemoveInventoryItem(Item);
      },
      // No charges capability: consumable items are implicitly single-use.
      () => owner.RemoveInventoryItem(Item));
  }
}
