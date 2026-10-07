using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleUnitReadoutTest
{
  [TestCase]
  public void ReadoutShowsCurrentStunAndUnconsciousness()
  {
    using var battle = BattleFixture.Duel();

    battle.ApplyDamage(battle.PlayerUnit, 5, DamageKind.Stun);
    string conscious = battle.Read(() => BattleHud.FormatUnitReadout(UnitReadout.Capture(battle.PlayerUnit)));
    Assert.True(conscious.Contains("STUN 5"));
    Assert.False(conscious.Contains("Unconscious"));

    battle.ApplyDamage(battle.PlayerUnit, 15, DamageKind.Stun);
    string unconscious = battle.Read(() => BattleHud.FormatUnitReadout(UnitReadout.Capture(battle.PlayerUnit)));
    Assert.True(unconscious.Contains("STUN 20"));
    Assert.True(unconscious.Contains("Unconscious"));
  }
}
