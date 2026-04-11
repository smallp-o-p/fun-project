namespace FunProject.Items;

public class ThrowableItem : EquippableItem
{
  public int ThrowRange { get; }
  public int ActionPointCost { get; }
  public bool ConsumesOnUse { get; }

  public ThrowableItem(ThrowableItemData data) : base(data)
  {
    ThrowRange = data.ThrowRange;
    ActionPointCost = data.ActionPointCost;
    ConsumesOnUse = data.ConsumesOnUse;
  }
}
