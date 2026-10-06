using FunProject.Battle;

namespace FunProject.Strategic;

/// <summary>The host owns the started runtime and its resolved setup.</summary>
public sealed record MissionBattle(MissionDeployment Deployment, BattleSetup Setup, BattleRuntime Runtime);
