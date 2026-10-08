using System.Collections.Generic;
using FunProject.Currencies;

namespace FunProject.Engineering;

public sealed record ManufacturingOption(ManufacturingProject Project, int Remaining,
  IReadOnlyList<(Currency Currency, long Required, long Available)> Shortfalls);
