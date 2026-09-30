using FunProject.Combatants;
using System.Collections.Frozen;

namespace FunProject.Battle;

/// <summary>One grouped end-of-battle report: outcome, per-faction result counts and
/// summaries, and the object tallies. Completion reads serve this single stored report.</summary>
public sealed class CompletedBattle
{
  private CompletedBattle(
    BattleOutcome outcome,
    int turnCount,
    SysColGeneric.IReadOnlyDictionary<Faction, FactionResultCounts> factions,
    SysColGeneric.IReadOnlyDictionary<Faction, FactionBattleSummary> factionSummaries,
    int objectsInteracted,
    int objectsExpired)
  {
    Outcome = outcome;
    TurnCount = turnCount;
    Factions = factions;
    FactionSummaries = factionSummaries;
    ObjectsInteracted = objectsInteracted;
    ObjectsExpired = objectsExpired;
  }

  public BattleOutcome Outcome { get; }
  public int TurnCount { get; }
  public SysColGeneric.IReadOnlyDictionary<Faction, FactionResultCounts> Factions { get; }
  public SysColGeneric.IReadOnlyDictionary<Faction, FactionBattleSummary> FactionSummaries { get; }
  public int ObjectsInteracted { get; }
  public int ObjectsExpired { get; }

  // One grouped pass at the terminal boundary: the pool is grouped by faction once and every
  // count, summary, kill list, and health report derives from those groups, so later
  // campaign-side mutations (faction reassignment, stat or health changes) cannot rewrite any
  // issued report. Campaign Combatant/Faction references are preserved; only numeric values
  // and collection membership are snapshotted, and every exposed container is frozen.
  internal static CompletedBattle Capture(BattleState state, BattleOutcome outcome, int turnCount)
  {
    var rosters = new SysColGeneric.Dictionary<Faction, SysColGeneric.List<BattleUnitState>>();
    foreach (BattleUnitState unit in state.Units)
    {
      if (!rosters.TryGetValue(unit.Side, out SysColGeneric.List<BattleUnitState>? roster))
        rosters[unit.Side] = roster = [];
      roster.Add(unit);
    }

    // Victory captures: living unconscious combatants of every non-player faction,
    // reference-deduplicated in pool order; only the designated player's summary carries them.
    SysColGeneric.List<Combatant> capturedEnemies = [];
    if (outcome == BattleOutcome.Victory)
    {
      state.PlayerFaction.IfSome(player =>
      {
        var captured = new SysColGeneric.HashSet<Combatant>(SysColGeneric.ReferenceEqualityComparer.Instance);
        foreach (BattleUnitState unit in state.Units)
          if (unit.Side != player && unit.IsUnconscious && captured.Add(unit.Combatant))
            capturedEnemies.Add(unit.Combatant);
      });
    }
    SysColGeneric.IReadOnlyList<Combatant> frozenCaptures = System.Array.AsReadOnly([.. capturedEnemies]);

    var factions = new SysColGeneric.Dictionary<Faction, FactionResultCounts>();
    var summaries = new SysColGeneric.Dictionary<Faction, FactionBattleSummary>();
    foreach (Faction faction in state.Factions)
    {
      SysColGeneric.List<BattleUnitState> roster = rosters.TryGetValue(faction, out SysColGeneric.List<BattleUnitState>? members)
        ? members
        : [];

      int killed = 0;
      var present = new SysColGeneric.HashSet<Combatant>();
      var dead = new SysColGeneric.HashSet<Combatant>();
      var wounded = new SysColGeneric.HashSet<Combatant>();
      var health = new SysColGeneric.Dictionary<Combatant, BattleHealthSummary>();
      foreach (BattleUnitState unit in roster)
      {
        present.Add(unit.Combatant);
        if (unit.IsDead)
        {
          killed++;
          dead.Add(unit.Combatant);
          continue;
        }

        if (unit.MaxHealth > unit.CurrentHealth)
          wounded.Add(unit.Combatant);
        health[unit.Combatant] = new BattleHealthSummary(unit.MaxHealth, unit.TotalHealthDamageTaken);
      }

      var defeated = state.KillsByUnit.AsValueEnumerable()
        .Where(entry => entry.Key.Side == faction)
        .ToDictionary(
          entry => entry.Key.Combatant,
          entry => (SysColGeneric.IReadOnlyList<Combatant>)System.Array.AsReadOnly(
            entry.Value.AsValueEnumerable().Select(killedUnit => killedUnit.Combatant).ToArray()));

      factions[faction] = new FactionResultCounts(roster.Count, killed);
      summaries[faction] = new FactionBattleSummary
      {
        Faction = faction,
        Outcome = outcome,
        CapturedEnemies = state.PlayerFaction == Some(faction)
          ? frozenCaptures
          : System.Array.AsReadOnly<Combatant>([]),
        CombatantsPresent = present.ToFrozenSet(),
        CombatantsDead = dead.ToFrozenSet(),
        CombatantsWounded = wounded.ToFrozenSet(),
        DefeatedPerCombatant = defeated.ToFrozenDictionary(),
        TurnCount = turnCount,
        HealthByCombatant = health.ToFrozenDictionary(),
      };
    }

    int objectsInteracted = 0;
    int objectsExpired = 0;
    foreach (BattleObjectState obj in state.Objects)
    {
      if (obj.Status == Some(ObjectStatus.Interacted))
        objectsInteracted++;
      else if (obj.Status == Some(ObjectStatus.Expired))
        objectsExpired++;
    }

    return new CompletedBattle(
      outcome,
      turnCount,
      factions.ToFrozenDictionary(),
      summaries.ToFrozenDictionary(),
      objectsInteracted,
      objectsExpired);
  }
}
