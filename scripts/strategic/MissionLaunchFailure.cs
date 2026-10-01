using FunProject.Battle;

namespace FunProject.Strategic;

/// <summary>Typed reasons a mission launch fails before any association exists.</summary>
public enum MissionLaunchFailureReason
{
  NoPendingMission,
  WrongPendingMission,
  AlreadyLaunched,
  EmptySquad,
  DuplicateCombatant,
  NotInRoster,
  FactionMismatch,
  SquadTooLarge,
  UnitUnavailable,
  SetupFailed,
}

/// <summary>A typed launch failure; setup faults retain the original battle failure.</summary>
public sealed record MissionLaunchFailure(
  MissionLaunchFailureReason Reason,
  string Message,
  Option<BattleSetupFailure> SetupFailure = default);
