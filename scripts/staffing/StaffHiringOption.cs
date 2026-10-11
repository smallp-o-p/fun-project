using FunProject.Currencies;

namespace FunProject.Staffing;

public sealed record StaffHiringOption(int Count, long RateUnits, uint BonusUnits,
  Godot.Collections.Dictionary<Currency, ulong> Costs, bool Affordable);
