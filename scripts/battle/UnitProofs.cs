using FunProject.Combatants;
using System;

namespace FunProject.Battle;

// One-shot proofs (same minting recipe as ItemWith<TCap>): only BattleSession mints these.
// A proof is a receipt that the facts held AT MINT — the unit belonged to this session's
// alive storage, standing at Position. It holds no reference back into the session and
// guarantees nothing past the mint: held across an executor commit its snapshot may be
// stale. Mint fresh per interaction; use within one synchronous scope that submits nothing.
public readonly struct AliveUnit
{
  public BattleUnitState State { get; }
  // Board position at mint (total: an alive session unit is always board-indexed).
  public BattleBoardState.ValidatedPoint Position { get; }

  internal AliveUnit(BattleUnitState state, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(state);
    State = state;
    Position = position;
  }

  public int Id => State.Id;
  public Faction Side => State.Side;
}

// Dead is monotone-true (no resurrection): this receipt cannot even go stale.
public readonly struct DeadUnit
{
  public BattleUnitState State { get; }

  internal DeadUnit(BattleUnitState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    State = state;
  }
}
