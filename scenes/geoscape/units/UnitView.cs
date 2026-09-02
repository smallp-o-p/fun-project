using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using Godot;
using System;

// Full-screen soldier screen: stats page (base -> effective), equipment slots, and the
// campaign armory browser. Presentation only — every interaction calls domain operations
// on GameState/Combatant/Armory and re-presents; nothing is cached beyond the bound unit.
public sealed partial class UnitView : PanelContainer, IGeoscapeView
{
  private Action? _requestClose;

  public void ArmClose(Action requestClose) => _requestClose = requestClose;

  public void Present(CampaignGameState state, Combatant unit)
  {
    // Filled in by the UnitView implementation task.
  }
}
