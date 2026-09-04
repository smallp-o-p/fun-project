using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Progression;
using FunProject.Stats;
using GdUnit4;
using Godot;
using static FunProject.Tests.ProgressionTestFactory;

[TestSuite]
[RequireGodotRuntime]
public partial class ProgressionBattleApplicationTest
{
  [TestCase(TestName = "Unlocked stat-mod step raises the spawned unit's effective aim")]
  public void UnlockedStatModAppliesAtSpawn()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var combatant = MakeCombatant("Alpha", faction, aim: 65);
    CommitAndUnlock(combatant, MakePath("Marksman",
      MakeStep(1, MakeStatModEffect(new AimStatMod { Modifiers = [StatModifier.Add(10)] }))));

    var unit = SpawnUnit(session, combatant, new Vector3I(4, 0, 4));
    Assert.Equal(75f, unit.State.EffectiveStat<AimStat>());
  }

  [TestCase(TestName = "Unlocked health step raises spawn MaxHealth and CurrentHealth")]
  public void UnlockedHealthModRaisesSpawnHealth()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var combatant = MakeCombatant("Bravo", faction, health: 20);
    CommitAndUnlock(combatant, MakePath("Vitality",
      MakeStep(1, MakeStatModEffect(new HealthStatMod { Modifiers = [StatModifier.Add(5)] }))));

    var unit = SpawnUnit(session, combatant, new Vector3I(4, 0, 4));
    Assert.Equal(25, unit.State.MaxHealth);
    Assert.Equal(25, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "Unlocked buff grants surface via InnateBuffs and stack with innate grants")]
  public void UnlockedBuffGrantSurfacesAndStacks()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var condition = new HealthBelowPercentConditionData { Percent = 50f };
    var shared = MakeBuff("Shared", condition);
    var innateOnly = MakeBuff("Innate", condition);
    var combatant = MakeCombatant("Charlie", faction, buffs: [shared, innateOnly]);

    Assert.Equal(2, combatant.InnateBuffs.Count); // authored innate only, before training

    CommitAndUnlock(combatant, MakePath("Guardian",
      MakeStep(1, MakeBuffGrantEffect(shared, MakeBuff("Learned", condition)))));

    // InnateBuffs is the single surface: authored innate ++ trained grants, live-computed.
    Assert.Equal(4, combatant.InnateBuffs.Count); // shared, innateOnly, shared, learned
    Assert.Equal(2, combatant.InnateBuffs.AsValueEnumerable().Count(buff => buff == shared));

    var unit = SpawnUnit(session, combatant, new Vector3I(4, 0, 4));
    Assert.Equal(4, unit.State.Buffs.Count); // identical buffs stack, one instance per grant
    Assert.Equal(2, unit.State.Buffs.AsValueEnumerable().Count(buff => buff.Data == shared));
  }

  private static void CommitAndUnlock(Combatant combatant, SkillPathData path)
  {
    combatant.Progression.AwardPoints(path.Steps[0].Cost);
    combatant.Progression.TryCommit(path);
    combatant.Progression.TryUnlockNext(path).RequireSome();
  }
}
