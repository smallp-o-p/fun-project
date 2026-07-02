using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class StatusEffectTest
{
  [TestCase(TestName = "Packets carry their authored status spec into the emitted bundle")]
  public void PacketsCarryStatusSpecIntoBundle()
  {
    var burn = MakeBurn();
    var frame = new WeaponFrameData { Name = "Frame", Packets = [] };
    frame.Packets.Add(new DamagePacketData { Element = Element.Thermal, Multiplier = 1f, Status = burn });
    frame.Packets.Add(new DamagePacketData { Element = Element.Kinetic, Multiplier = 1f });
    var weapon = MakeWeapon("Torch", frame: frame);

    var bundle = weapon.EmitDamage();

    Assert.Equal(2, bundle.Count);
    Assert.Equal(burn, bundle[0].Status.RequireSome());
    Assert.True(bundle[1].Status.IsNone);
  }

  private static BattleUnitState MakeUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    return new BattleUnitState(0, BattleTestFactory.MakeCombatant("Alpha", faction), None, None);
  }

  [TestCase(TestName = "Applying a status tracks it and re-applying refreshes the duration")]
  public void ApplyStatusTracksAndRefreshes()
  {
    var unit = MakeUnit();
    var burn = MakeBurn(duration: 3);

    var applied = unit.ApplyStatusEffect(burn);
    Assert.Equal(3, applied.RemainingTurns);

    applied.TickDown();
    Assert.Equal(2, applied.RemainingTurns);

    var refreshed = unit.ApplyStatusEffect(burn);
    Assert.Equal(1, unit.ActiveStatusEffects.Count);
    Assert.Equal(3, refreshed.RemainingTurns);
  }

  [TestCase(TestName = "Distinct spec resources are distinct statuses")]
  public void DistinctSpecResourcesAreDistinctStatuses()
  {
    var unit = MakeUnit();
    unit.ApplyStatusEffect(MakeBurn());
    unit.ApplyStatusEffect(MakeBurn());

    Assert.Equal(2, unit.ActiveStatusEffects.Count);
  }

  [TestCase(TestName = "IsImmobilized tracks active immobilize statuses")]
  public void IsImmobilizedTracksActiveImmobilizeStatuses()
  {
    var unit = MakeUnit();
    Assert.False(unit.IsImmobilized);

    var stun = MakeStun();
    unit.ApplyStatusEffect(stun);
    Assert.True(unit.IsImmobilized);

    unit.ApplyStatusEffect(MakeBurn());
    Assert.True(unit.IsImmobilized);

    Assert.True(unit.RemoveStatusEffect(stun));
    Assert.False(unit.IsImmobilized);
  }

  [TestCase(TestName = "An expired immobilize no longer immobilizes even before removal")]
  public void ExpiredImmobilizeNoLongerImmobilizes()
  {
    var unit = MakeUnit();
    var applied = unit.ApplyStatusEffect(MakeStun(duration: 1));
    Assert.True(unit.IsImmobilized);

    applied.TickDown();

    Assert.False(unit.IsImmobilized);
  }

  [TestCase(TestName = "Removing a status that is not active returns false")]
  public void RemovingInactiveStatusReturnsFalse()
  {
    var unit = MakeUnit();

    Assert.False(unit.RemoveStatusEffect(MakeStun()));
  }
}
