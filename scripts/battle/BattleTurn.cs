using FunProject.Combatants;

namespace FunProject.Battle;

/// <summary>Immutable snapshot of the scheduler's current turn: the active faction and round
/// number. A read value, never the mutable running receiver.</summary>
public sealed record BattleTurn(Faction ActiveFaction, int RoundNumber);
