using FunProject.Items;
using System.Collections.Generic;
using FunProject.Currencies;

namespace FunProject.Engineering;

public record struct ManufacturingProject(ManufacturingRecipe Recipe, EquippableItemData Item, uint DurationDays, bool UnlimitedStock,
  IReadOnlyDictionary<Currency, long> Costs);
