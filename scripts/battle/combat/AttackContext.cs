using FunProject.Weapons;

namespace FunProject.Battle;

// Callers are responsible for resolving AttackerPosition/DefenderPosition from
// the board's occupancy at construction time; the record cannot enforce that
// the positions match where the units actually stand.
public sealed record AttackContext(
  BattleUnitState Attacker,
  BattleUnitState Defender,
  BattleBoardState.ValidatedPoint AttackerPosition,
  BattleBoardState.ValidatedPoint DefenderPosition,
  Weapon Weapon,
  BattleBoardState Board);
