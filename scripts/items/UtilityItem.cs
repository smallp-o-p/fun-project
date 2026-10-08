namespace FunProject.Items;

/// <summary>Proof of authored utility-slot compatibility, minted by ForUtilitySlot().</summary>
public sealed class UtilityItem
{
  public EquippableItem Item { get; }

  internal UtilityItem(EquippableItem item) => Item = item;
}
