using FunProject.Battle;
using Godot;
using System;

public sealed partial class BattleActionResultAdapter(BattleActionResult res) : RefCounted
{
  public BattleActionResult Res { get; init; } = res;
  public string ActionId => Res.Action.ActionId;
  public bool Succeeded => Res.Succeeded;
  public string FailureReason => Res.FailureReason.ToString();
  public string Message => Res.Message;

  public Option<BattleUnitState> AffectedUnit => Res.AffectedUnit;
}
