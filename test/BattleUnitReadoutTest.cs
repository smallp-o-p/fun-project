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
    battle.Read(() => Assert.True(conscious.Contains("STUN 5")));
    battle.Read(() => Assert.False(conscious.Contains("Unconscious")));

    battle.ApplyDamage(battle.PlayerUnit, 15, DamageKind.Stun);
    string unconscious = battle.Read(() => BattleHud.FormatUnitReadout(UnitReadout.Capture(battle.PlayerUnit)));
    battle.Read(() => Assert.True(unconscious.Contains("STUN 20")));
    battle.Read(() => Assert.True(unconscious.Contains("Unconscious")));
  }
}
