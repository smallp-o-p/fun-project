using Godot;

namespace FunProject.Battle;

/// <summary>Authored registration for the turn-end object expiry system.</summary>
[GlobalClass]
public sealed partial class ObjectExpirySystemData : BattleTypeSystemData
{
  [Export] public int Priority { get; set; } = 0;

  public override void Register(BattleRuntime runtime)
    => runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem(), Priority);
}
