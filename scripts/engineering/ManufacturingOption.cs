using System.Collections.Generic;
using FunProject.Currencies;

namespace FunProject.Engineering;

public sealed record ManufacturingOption(ManufacturingProject Project, int Remaining,
  IReadOnlyList<CurrencyShortfall> Shortfalls);
