using FunProject.Items;

namespace FunProject.Engineering;

public record struct ManufacturingProject(EquippableItemData Item, uint DurationDays, bool UnlimitedStock);
