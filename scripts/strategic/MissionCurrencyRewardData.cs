using FunProject.Currencies;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public partial class MissionCurrencyRewardData : Resource
{
  [Export] public required Currency Currency { get; set; }
  [Export] public ulong Minimum { get; set; }
  [Export] public ulong Maximum { get; set; }
}
