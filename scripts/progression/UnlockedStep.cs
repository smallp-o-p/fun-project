namespace FunProject.Progression;

/// <summary>Result of a successful purchase: the step just unlocked (for UI feedback).</summary>
public sealed record UnlockedStep(SkillPathData Path, SkillUpgradeStepData Step);
