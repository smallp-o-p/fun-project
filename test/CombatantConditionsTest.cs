using System;
using FunProject.Combatants;
using FunProject.Combatants.Conditions;
using FunProject.Stats;
using FunProject.Strategic;
using GdUnit4;

// Direct condition-lifecycle tests: CombatantConditionSystem is a scene-independent
// campaign registry, so these exercise its record mutations directly (fresh system per
// case, TestData combatants as identities) instead of routing every scenario through a
// campaign fixture. Assertions use integer tier positions: 0 = Healthy/Fresh, position n
// = ladder resource index n-1; the default ladders are four injury (0..4) and three
// fatigue (0..3) rungs.
[TestSuite]
[RequireGodotRuntime]
public class CombatantConditionsTest
{
  [TestCase(0L, 100, 0)]
  [TestCase(24L, 100, 1)]
  [TestCase(25L, 100, 2)]
  [TestCase(49L, 100, 2)]
  [TestCase(50L, 100, 3)]
  [TestCase(74L, 100, 3)]
  [TestCase(75L, 100, 4)]
  // Exact ratios, not rounded percentages: 24/99 = 24.24%, 25/99 = 25.25%,
  // 74/99 = 74.74% (rounds to 75 -> wrongly position 4),
  // 24/96 = exactly 25% (truncates to 24 -> wrongly position 1).
  [TestCase(24L, 99, 1)]
  [TestCase(25L, 99, 2)]
  [TestCase(74L, 99, 3)]
  [TestCase(24L, 96, 2)]
  public void MissionReturnClassifiesExactDamageRatios(long damage, int maxHealth, int expectedTier)
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(unit, damage, maxHealth, 0);

    if (expectedTier == 0)
      Assert.True(system.GetInjury(unit).IsNone);
    else
      Assert.Equal(expectedTier, system.GetInjury(unit).RequireSome().Tier);
    // Even the zero-damage row earns its first fatigue step.
    Assert.Equal(1, system.GetFatigue(unit).RequireSome().Tier); // Tired
    Assert.Equal(1440L, system.GetFatigue(unit).RequireSome().RecoveryTick);
  }

  [TestCase(TestName = "An exact 29% threshold classifies 29/100 damage as earned, not drifted")]
  public void ExactThresholdRatiosClassifyWithoutDivideMultiplyDrift()
  {
    var rules = RulesWith(InjuryLadder(
      Tier("A", 1, 0f), Tier("B", 1, 29f), Tier("C", 1, 50f), Tier("D", 1, 75f)));
    var system = new CombatantConditionSystem(rules);
    var atThreshold = Unit();
    system.ApplyMissionReturn(atThreshold, 29, 100, 0);
    Assert.Equal(2, system.GetInjury(atThreshold).RequireSome().Tier);
    var belowThreshold = Unit();
    system.ApplyMissionReturn(belowThreshold, 28, 100, 0);
    Assert.Equal(1, system.GetInjury(belowThreshold).RequireSome().Tier);
  }

  [TestCase(TestName = "A one-day tier recovers in exactly 1440 ticks, never 60")]
  public void OneDayTierConvertsToExactly1440Ticks()
  {
    var rules = RulesWith(InjuryLadder(
      Tier("A", 1, 0f), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f)));
    var unit = Unit();
    var system = new CombatantConditionSystem(rules);
    system.ApplyMissionReturn(unit, 10, 100, 0);
    Assert.Equal(1, system.GetInjury(unit).RequireSome().Tier);
    Assert.Equal(1440L, system.GetInjury(unit).RequireSome().RecoveryTick);
    // One minute before the boundary nothing has recovered; an hour-based conversion
    // (60 ticks) would have cleared the injury long before this.
    RecoverOne(system, unit, 1439);
    Assert.Equal(1, system.GetInjury(unit).RequireSome().Tier);
    RecoverOne(system, unit, 1440);
    Assert.True(system.GetInjury(unit).IsNone);
  }

  [TestCase(TestName = "Each mission return adds its earned tiers, capped at the last authored tier")]
  public void EachMissionReturnAddsItsEarnedTiersCappedAtTheLastAuthoredTier()
  {
    // A five-rung ladder whose top rung sits at 90%: the cap comes from the data, so a
    // third heavy return clamps at position 5 instead of the default ladder's 4.
    var rules = RulesWith(InjuryLadder(
      Tier("A", 1, 0f), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f), Tier("E", 1, 90f)));
    var unit = Unit();
    var system = new CombatantConditionSystem(rules);
    system.ApplyMissionReturn(unit, 40, 100, 0);
    Assert.Equal(2, system.GetInjury(unit).RequireSome().Tier);
    // A second 40% return earns two more tiers: 2 + 2 = 4.
    system.ApplyMissionReturn(unit, 40, 100, 10);
    Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);
    // Further earned tiers clamp at the fifth authored tier instead of exceeding it.
    system.ApplyMissionReturn(unit, 60, 100, 20);
    Assert.Equal(5, system.GetInjury(unit).RequireSome().Tier);
  }

  [TestCase(10, 10, 1, 2, 8700L)]
  [TestCase(25, 25, 2, 4, 43260L)]
  [TestCase(10, 0, 1, 1, 5760L)]
  public void RepeatedReturnsAccumulateOrPreserveInjury(
    int firstDamage, int secondDamage, int firstTier, int expectedTier, long expectedDeadline)
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(unit, firstDamage, 100, 0);
    Assert.Equal(firstTier, system.GetInjury(unit).RequireSome().Tier);

    system.ApplyMissionReturn(unit, secondDamage, 100, 60);
    Assert.Equal(expectedTier, system.GetInjury(unit).RequireSome().Tier);
    // The zero-damage row keeps its original deadline instead of restarting at tick 60.
    Assert.Equal(expectedDeadline, system.GetInjury(unit).RequireSome().RecoveryTick);
    Assert.Equal(1, system.GetFatigue(unit).RequireSome().Tier); // Tired — already injured before the second return
    Assert.Equal(1440L, system.GetFatigue(unit).RequireSome().RecoveryTick);
  }

  [TestCase(TestName = "Damage after partial recovery adds onto the recovered level")]
  public void DamageAfterPartialRecoveryAddsOntoCurrentLevel()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(unit, 80, 100, 0);
    RecoverOne(system, unit, 74880);
    Assert.Equal(1, system.GetInjury(unit).RequireSome().Tier);
    // A fresh 1% mission earns one tier on top of the recovered level, not the history.
    system.ApplyMissionReturn(unit, 1, 100, 74881);
    Assert.Equal(2, system.GetInjury(unit).RequireSome().Tier);
    Assert.Equal(83521L, system.GetInjury(unit).RequireSome().RecoveryTick);
  }

  [TestCase(TestName = "An exhausted return resets its own 11520-tick fatigue deadline")]
  public void ExhaustedReturnResetsItsFatigueDeadline()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    for (int i = 0; i < 3; i++)
      system.ApplyMissionReturn(unit, 0, 100, 0);
    Assert.Equal(3, system.GetFatigue(unit).RequireSome().Tier); // Exhausted
    Assert.Equal(11520L, system.GetFatigue(unit).RequireSome().RecoveryTick);
    system.ApplyMissionReturn(unit, 0, 100, 100);
    Assert.Equal(3, system.GetFatigue(unit).RequireSome().Tier);
    Assert.Equal(11620L, system.GetFatigue(unit).RequireSome().RecoveryTick);
  }

  // Fresh registry per case. In the 50000 case the first recovery call is 50000 itself on
  // the fresh 80/100 seed, so the re-anchored first deadline (43200 + 23040, not
  // 50000 + 23040) is what the tier 3/66240 assertions detect; the pre-deadline probe
  // below runs only in the 43200 case.
  [TestCase(43200L)]
  [TestCase(50000L)]
  public void CriticalInjuryRecoveryPreservesChainedDeadlines(long firstRecoveryTick)
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(unit, 80, 100, 0);
    Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);

    if (firstRecoveryTick == 43200L)
    {
      // One tick before the first deadline nothing has recovered (`<` versus `<=`).
      RecoverOne(system, unit, 43199);
      Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);
      Assert.Equal(43200L, system.GetInjury(unit).RequireSome().RecoveryTick);
    }

    // Deadlines chain on the previous deadline, not the recovery call's tick; 0 marks the
    // record's removal.
    (long Tick, int Tier, long Deadline, float ExpectedAim)[] steps =
    [
      (firstRecoveryTick, 3, 66240L, 80f),
      (66240L, 2, 74880L, 90f),
      (74880L, 1, 80640L, 95f),
      (80640L, 0, 0L, 100f),
    ];
    foreach ((long tick, int tier, long deadline, float expectedAim) in steps)
    {
      RecoverOne(system, unit, tick);
      if (tier == 0)
        Assert.True(system.GetInjury(unit).IsNone);
      else
      {
        Assert.Equal(tier, system.GetInjury(unit).RequireSome().Tier);
        Assert.Equal(deadline, system.GetInjury(unit).RequireSome().RecoveryTick);
      }
      Assert.Equal(expectedAim, unit.Resolve<AimStat>(system.StatContributions(unit)));
    }
  }

  [TestCase(TestName = "Fatigue recovers through its 1440/5760/11520 chain")]
  public void FatigueRecoversThroughItsChain()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    for (int i = 0; i < 3; i++)
      system.ApplyMissionReturn(unit, 0, 100, 0);
    Assert.Equal(3, system.GetFatigue(unit).RequireSome().Tier);
    Assert.Equal(11520L, system.GetFatigue(unit).RequireSome().RecoveryTick);

    RecoverOne(system, unit, 11520);
    Assert.Equal(2, system.GetFatigue(unit).RequireSome().Tier);
    Assert.Equal(17280L, system.GetFatigue(unit).RequireSome().RecoveryTick);

    RecoverOne(system, unit, 17280);
    Assert.Equal(1, system.GetFatigue(unit).RequireSome().Tier);
    Assert.Equal(18720L, system.GetFatigue(unit).RequireSome().RecoveryTick);

    RecoverOne(system, unit, 18720);
    Assert.True(system.GetFatigue(unit).IsNone);
  }

  [TestCase(TestName = "Recovery on an already-healthy subject is a no-op")]
  public void RecoveryOnHealthySubjectIsANoOp()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    RecoverOne(system, unit, 99999);
    Assert.True(system.GetInjury(unit).IsNone);
    Assert.True(system.GetFatigue(unit).IsNone);
  }

  [TestCase(TestName = "Condition names follow the current tiers and clear at position 0")]
  public void NamesFollowCurrentTiers()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    Assert.Equal("Healthy", system.InjuryName(0));
    Assert.Equal("Fresh", system.FatigueName(0));
    system.ApplyMissionReturn(unit, 10, 100, 0);
    Assert.Equal("Lightly Injured", system.InjuryName(system.GetInjury(unit).RequireSome().Tier));
    Assert.Equal("Tired", system.FatigueName(system.GetFatigue(unit).RequireSome().Tier));
    RecoverOne(system, unit, 5760);
    // Absence maps to position 0 for the name lookup only: this case tests labels.
    Assert.Equal("Healthy", system.InjuryName(system.GetInjury(unit).Map(injury => injury.Tier).IfNone(0)));
    Assert.Equal("Fresh", system.FatigueName(system.GetFatigue(unit).Map(fatigue => fatigue.Tier).IfNone(0)));
  }

  [TestCase(TestName = "One late recovery call settles both conditions concurrently")]
  public void LateRecoverySettlesBothConditionsConcurrently()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    // Fatigue is built before the injury: returns while injured add no fatigue.
    for (int i = 0; i < 2; i++)
      system.ApplyMissionReturn(unit, 0, 100, 0);
    system.ApplyMissionReturn(unit, 80, 100, 0);
    Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);
    Assert.Equal(3, system.GetFatigue(unit).RequireSome().Tier);

    RecoverOne(system, unit, 80640);
    Assert.True(system.GetInjury(unit).IsNone);
    Assert.True(system.GetFatigue(unit).IsNone);
  }

  [TestCase(TestName = "Damage taken after a full recovery starts a fresh injury")]
  public void DamageAfterFullRecoveryStartsFresh()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(unit, 80, 100, 0);
    RecoverOne(system, unit, 80640);
    Assert.True(system.GetInjury(unit).IsNone);
    system.ApplyMissionReturn(unit, 10, 100, 80640);
    Assert.Equal(1, system.GetInjury(unit).RequireSome().Tier);
  }

  [TestCase(TestName = "Fatigue modifiers follow the fatigue tiers and clear when rested")]
  public void FatigueModifiersFollowFatigueTiers()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(unit, 0, 100, 0);
    Assert.Equal(1, system.GetFatigue(unit).RequireSome().Tier);
    Assert.Equal(90f, unit.Resolve<AimStat>(system.StatContributions(unit)));
    Assert.Equal(90f, unit.Resolve<WillStat>(system.StatContributions(unit)));
    RecoverOne(system, unit, 1440);
    Assert.Equal(100f, unit.Resolve<AimStat>(system.StatContributions(unit)));
    Assert.Equal(100f, unit.Resolve<WillStat>(system.StatContributions(unit)));
  }

  [TestCase(TestName = "A fresh combatant's stats are unchanged by conditions")]
  public void FreshStatsAreUnchanged()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    Option<InjuryState> injury = system.GetInjury(unit);
    Option<FatigueState> fatigue = system.GetFatigue(unit);
    Assert.True(injury.IsNone);
    Assert.True(fatigue.IsNone);
    Assert.Equal(100f, unit.Resolve<HealthStat>(system.StatContributions(unit)));
    Assert.Equal(100f, unit.Resolve<AimStat>(system.StatContributions(unit)));
    Assert.Equal(100f, unit.Resolve<WillStat>(system.StatContributions(unit)));
    Assert.Equal(100f, unit.Resolve<MovementStat>(system.StatContributions(unit)));
  }

  [TestCase(false)]
  [TestCase(true)]
  public void ActiveInjuryFloorsOneHealthCombatant(bool authoredWithoutCaps)
  {
    CombatantConditionRulesData? rules = null;
    if (authoredWithoutCaps)
    {
      // Four distinct one-day tiers, all multiplying Health by 0.4, with no authored cap.
      (string Name, float Threshold)[] tiers =
        [("Grazed", 0f), ("Wounded", 25f), ("Broken", 50f), ("Doomed", 75f)];
      var injuryTiers = new Godot.Collections.Array<InjuryTierData>();
      foreach ((string name, float threshold) in tiers)
        injuryTiers.Add(new InjuryTierData
        {
          Name = name,
          RecoveryDays = 1,
          MinimumDamagePercent = threshold,
          StatMods = Mods(Multiply<HealthStatMod>(0.4f)),
        });
      rules = RulesWith(injuryTiers);
    }

    var unit = Unit(health: 1);
    var system = new CombatantConditionSystem(rules);
    system.ApplyMissionReturn(unit, 1, 1, 0);
    Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);
    // 0.4 max health folds to zero without the runtime's unconditional CapMin floor.
    Assert.Equal(1f, unit.Resolve<HealthStat>(system.StatContributions(unit)));
  }

  [TestCase(0)]
  [TestCase(2)]
  public void CriticalInjuryStatsComposeWithFatigue(int priorReturns)
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    // Fatigue is built before the injury: returns while injured add no fatigue, so with
    // two prior returns the injury return itself adds the final Exhausted step.
    for (int i = 0; i < priorReturns; i++)
      system.ApplyMissionReturn(unit, 0, 100, 0);
    system.ApplyMissionReturn(unit, 80, 100, 0);

    Assert.Equal(40f, unit.Resolve<HealthStat>(system.StatContributions(unit)));
    if (priorReturns == 2)
    {
      Assert.Equal(45.5f, unit.Resolve<AimStat>(system.StatContributions(unit)));
      Assert.Equal(65f, unit.Resolve<WillStat>(system.StatContributions(unit)));
      Assert.Equal(70f, unit.Resolve<MovementStat>(system.StatContributions(unit)));
    }
  }

  [TestCase(TestName = "Authored condition rules customize names, durations, and mods")]
  public void AuthoredRulesCustomizeNamesDurationsAndMods()
  {
    var rules = new CombatantConditionRulesData
    {
      // Authored out of order and shorter than the defaults: arbitrary tier counts, with
      // injury shuffled against its thresholds and fatigue against its recovery days.
      InjuryTiers = new Godot.Collections.Array<InjuryTierData>
      {
        new() { Name = "Broken", RecoveryDays = 3, MinimumDamagePercent = 20f, StatMods = Mods(Multiply<AimStatMod>(0.3f)) },
        new() { Name = "Grazed", RecoveryDays = 1, MinimumDamagePercent = 0f, StatMods = Mods(Multiply<AimStatMod>(0.5f)) },
        new() { Name = "Wounded", RecoveryDays = 2, MinimumDamagePercent = 10f, StatMods = Mods(Multiply<AimStatMod>(0.4f)) },
      },
      FatigueTiers = new Godot.Collections.Array<ConditionTierData>
      {
        new() { Name = "Spent", RecoveryDays = 5, StatMods = Mods(Multiply<WillStatMod>(0.3f)) },
        new() { Name = "Winded", RecoveryDays = 1, StatMods = Mods(Multiply<WillStatMod>(0.5f)) },
      },
    };
    var unit = Unit();
    var system = new CombatantConditionSystem(rules);
    system.ApplyMissionReturn(unit, 5, 100, 0);
    Assert.Equal(1, system.GetInjury(unit).RequireSome().Tier);
    Assert.Equal("Grazed", system.InjuryName(system.GetInjury(unit).RequireSome().Tier));
    Assert.Equal(1440L, system.GetInjury(unit).RequireSome().RecoveryTick);
    Assert.Equal(50f, unit.Resolve<AimStat>(system.StatContributions(unit)));
    Assert.Equal("Winded", system.FatigueName(system.GetFatigue(unit).RequireSome().Tier));
    Assert.Equal(50f, unit.Resolve<WillStat>(system.StatContributions(unit)));
  }

  [TestCase(TestName = "Combatants in one system keep independent condition records")]
  public void CombatantsInOneSystemHaveIndependentRecords()
  {
    var faction = TestData.MakeFaction("Player");
    var first = TestData.MakeCombatant("Alpha", faction, health: 100);
    var second = TestData.MakeCombatant("Beta", faction, health: 100);
    var system = new CombatantConditionSystem();
    system.ApplyMissionReturn(first, 80, 100, 0);
    Assert.Equal(4, system.GetInjury(first).RequireSome().Tier);
    Assert.True(system.GetInjury(second).IsNone);
    Assert.Equal(40f, first.Resolve<HealthStat>(system.StatContributions(first)));
    Assert.Equal(100f, second.Resolve<HealthStat>(system.StatContributions(second)));
  }

  [TestCase(TestName = "Malformed condition rules are rejected by the rules resource")]
  public void MalformedRulesAreRejectedByTheRulesResource()
  {
    static CombatantConditionRulesData WithNullMod()
    {
      InjuryTierData broken = Tier("A", 1, 0f);
      broken.StatMods.Add(null!);
      return RulesWith(InjuryLadder(broken, Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f)));
    }

    // Tier counts, orderings, and nonzero first thresholds are authored freely; what
    // stays malformed is unusable tier data itself.
    CombatantConditionRulesData[] malformed =
    [
      RulesWith(InjuryLadder(Tier("A", 0, 0f), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f))),
      RulesWith(InjuryLadder(Tier("", 1, 0f), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f))),
      WithNullMod(),
      RulesWith(InjuryLadder(Tier("A", 1, 0f), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 101f))),
      RulesWith(InjuryLadder(Tier("A", 1, float.NaN), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f))),
    ];
    foreach (CombatantConditionRulesData rules in malformed)
      Assert.Throws<InvalidOperationException>(rules.ValidateAndSort);
  }

  [TestCase(TestName = "Deadline overflow throws and leaves the current record unchanged")]
  public void DeadlineOverflowLeavesRecordUnchanged()
  {
    var unit = Unit();
    var system = new CombatantConditionSystem();
    Assert.Throws<OverflowException>(() => system.ApplyMissionReturn(unit, 80, 100, long.MaxValue - 3000));
    Assert.True(system.GetInjury(unit).IsNone);

    system.ApplyMissionReturn(unit, 80, 100, long.MaxValue - 500 - 43200);
    Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);
    Assert.Throws<OverflowException>(() => system.Recover([unit], long.MaxValue - 400));
    Assert.Equal(4, system.GetInjury(unit).RequireSome().Tier);
    Assert.Equal(long.MaxValue - 500, system.GetInjury(unit).RequireSome().RecoveryTick);
  }

  [TestCase(TestName = "CanDeploy gates on injury and exhaustion; the flag overrides both")]
  public void CanDeployGatesOnInjuryAndExhaustion()
  {
    // Positions 0..4 across the default four-rung injury ladder and 0..3 across the
    // three-rung fatigue ladder, driven by the authored array counts.
    int injuryPositions = CombatantConditionRulesData.Shared.InjuryTiers.Count + 1;
    for (int position = 0; position < injuryPositions; position++)
    {
      var (system, unit) = InjuredTo(position);
      Assert.Equal(position == 0, system.CanDeploy(unit, false));
      Assert.True(system.CanDeploy(unit, true));
    }
    int fatiguePositions = CombatantConditionRulesData.Shared.FatigueTiers.Count + 1;
    for (int position = 0; position < fatiguePositions; position++)
    {
      var (system, unit) = FatiguedTo(position);
      // Only the ladder's top fatigue tier (the default Exhausted) blocks deployment.
      Assert.Equal(position < fatiguePositions - 1, system.CanDeploy(unit, false));
      Assert.True(system.CanDeploy(unit, true));
    }
  }

  [TestCase(TestName = "Authored edits after construction flow through the bound resources")]
  public void AuthoredEditsAfterConstructionFlowThroughBoundResources()
  {
    // The system holds no captured copy: it reads the tier resources directly, so field
    // and mod-array edits reach it. Production treats the resources as immutable after
    // initialization; this case deliberately mutates them to prove the reference identity.
    var rules = new CombatantConditionRulesData();
    var unit = Unit();
    var bound = new CombatantConditionSystem(rules);
    rules.InjuryTiers[0].Name = "Hacked";
    rules.InjuryTiers[0].RecoveryDays = 999;
    rules.InjuryTiers[1].MinimumDamagePercent = 40;
    rules.InjuryTiers[0].StatMods.Add(Multiply<HealthStatMod>(0.1f));
    rules.FatigueTiers[0].StatMods.Add(Multiply<AimStatMod>(0.1f));

    bound.ApplyMissionReturn(unit, 10, 100, 0);
    Assert.Equal(1, bound.GetInjury(unit).RequireSome().Tier);
    Assert.Equal("Hacked", bound.InjuryName(bound.GetInjury(unit).RequireSome().Tier)); // the live name
    Assert.Equal(1438560L, bound.GetInjury(unit).RequireSome().RecoveryTick); // the live 999 days
    StatMod[] contributions = [.. bound.StatContributions(unit)];
    // 4 live injury mods (3 authored + the late one) + the unconditional floor + 3 live
    // Tired mods (2 authored + the late one).
    Assert.Equal(8, contributions.Length);

    // The live thresholds classify: with tier 2 now at 40, a 30% return earns one tier.
    bound.ApplyMissionReturn(unit, 30, 100, 10);
    Assert.Equal(2, bound.GetInjury(unit).RequireSome().Tier);
    Assert.Equal(8650L, bound.GetInjury(unit).RequireSome().RecoveryTick); // tier 2's 6 days
  }

  private static Combatant Unit(int health = 100)
    => TestData.MakeCombatant("Alpha", TestData.MakeFaction("Player"), health: health,
      aim: 100, will: 100, movement: 100);

  private static void RecoverOne(CombatantConditionSystem system, Combatant unit, long tick)
    => system.Recover([unit], tick);

  private static (CombatantConditionSystem System, Combatant Unit) InjuredTo(int position)
  {
    var system = new CombatantConditionSystem();
    var unit = Unit();
    long damage = position switch
    {
      0 => 0,
      1 => 10,
      2 => 30,
      3 => 60,
      _ => 90,
    };
    system.ApplyMissionReturn(unit, damage, 100, 0);
    return (system, unit);
  }

  private static (CombatantConditionSystem System, Combatant Unit) FatiguedTo(int position)
  {
    var system = new CombatantConditionSystem();
    var unit = Unit();
    for (int i = 0; i < position; i++)
      system.ApplyMissionReturn(unit, 0, 100, 0);
    return (system, unit);
  }

  private static StatMod Multiply<TMod>(float multiplier) where TMod : StatMod, new()
  {
    var mod = new TMod();
    mod.AddModifier(StatModifier.Multiply(multiplier));
    return mod;
  }

  private static Godot.Collections.Array<StatMod> Mods(params StatMod[] mods) => new(mods);

  private static InjuryTierData Tier(string name, uint days, float minPercent)
    => new() { Name = name, RecoveryDays = days, MinimumDamagePercent = minPercent };

  private static ConditionTierData FatigueTier(string name, uint days)
    => new() { Name = name, RecoveryDays = days };

  private static Godot.Collections.Array<InjuryTierData> InjuryLadder(params InjuryTierData[] tiers)
    => tiers.Length == 0
      ? new Godot.Collections.Array<InjuryTierData> { Tier("A", 1, 0f), Tier("B", 1, 25f), Tier("C", 1, 50f), Tier("D", 1, 75f) }
      : new Godot.Collections.Array<InjuryTierData>(tiers);

  private static Godot.Collections.Array<ConditionTierData> FatigueLadder(params ConditionTierData[] tiers)
    => tiers.Length == 0
      ? new Godot.Collections.Array<ConditionTierData> { FatigueTier("X", 1), FatigueTier("Y", 1), FatigueTier("Z", 1) }
      : new Godot.Collections.Array<ConditionTierData>(tiers);

  private static CombatantConditionRulesData RulesWith(
    Godot.Collections.Array<InjuryTierData>? injuryTiers = null,
    Godot.Collections.Array<ConditionTierData>? fatigueTiers = null)
    => new() { InjuryTiers = injuryTiers ?? InjuryLadder(), FatigueTiers = fatigueTiers ?? FatigueLadder() };
}
