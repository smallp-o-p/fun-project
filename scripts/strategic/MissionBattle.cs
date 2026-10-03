using FunProject.Battle;
using System;

namespace FunProject.Strategic;

/// <summary>Host-side handle for one live mission battle: the deployment it belongs to, the
/// setup it started from, and the runtime the host now owns. Pure data — it never creates or
/// disposes an executor and is never stored in persisted campaign state; the runtime's
/// lifetime belongs to the host.</summary>
public sealed class MissionBattle
{
  internal MissionBattle(MissionDeployment deployment, BattleSetup setup, BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(deployment);
    ArgumentNullException.ThrowIfNull(setup);
    ArgumentNullException.ThrowIfNull(runtime);

    Deployment = deployment;
    Setup = setup;
    Runtime = runtime;
  }

  public MissionDeployment Deployment { get; }

  public BattleSetup Setup { get; }

  public BattleRuntime Runtime { get; }
}
