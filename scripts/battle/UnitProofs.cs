using FunProject.Combatants;
using System;

namespace FunProject.Battle;

// Snapshot proofs: only BattleSession mints these. Repeated synchronous reads are valid
// until a battle mutation; FPF001 checks freshness through copies and query wrappers.
// A proof is a receipt that the facts held AT MINT — the unit belonged to this session's
// alive storage, standing at Position. It holds no reference back into the session and
// supports repeated reads until mutation: held across an executor commit its snapshot may be
// stale. Mint fresh per interaction; use within one synchronous scope that submits nothing.
[SnapshotProof]
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

/// <summary>Receipt that an object was placed in this session at the captured position.</summary>
[SnapshotProof]
public readonly struct LiveObject
{
  /// <summary>The runtime object state proven alive at mint time.</summary>
  public BattleObjectState State { get; }
  /// <summary>The object's board position captured when the proof was minted.</summary>
  public BattleBoardState.ValidatedPoint Position { get; }

  internal LiveObject(BattleObjectState state, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(state);
    State = state;
    Position = position;
  }

  /// <summary>The stable runtime identifier of the proven object.</summary>
  public int Id => State.Id;
}

/// <summary>
/// Receipt that an entity (unit or board object) was a targetable member of this session at
/// mint: alive/live and standing at <see cref="Position"/>, a snapshot just like
/// <see cref="AliveUnit.Position"/>. It holds no reference back into the session and
/// supports repeated reads until mutation: held across an executor commit its snapshot may be
/// stale. Mint fresh per interaction; use within one synchronous scope that submits nothing.
/// </summary>
[SnapshotProof]
public readonly struct AttackTarget
{
  public BattleEntity Entity { get; }
  public BattleBoardState.ValidatedPoint Position { get; }

  internal AttackTarget(BattleEntity entity, BattleBoardState.ValidatedPoint position)
  {
    ArgumentNullException.ThrowIfNull(entity);
    Entity = entity;
    Position = position;
  }
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
