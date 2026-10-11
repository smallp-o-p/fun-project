using FunProject.Currencies;

namespace FunProject.Staffing;

public sealed record StaffHiringOption(ulong Count, double Speed, float SpeedBonus,
  Godot.Collections.Dictionary<Currency, ulong> Costs, bool Affordable);
