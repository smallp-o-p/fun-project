using FunProject.Battle;
using FunProject.Items.Effects;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class BattleObjectTest
{
  private static BattleSpecialObjectData MakeBombData(int expireAfterTurns = 5, int effectRadius = 2)
  {
    var data = new BattleSpecialObjectData { Name = "Alien Bomb" };
    data.Capabilities.Add(new InteractiveCapabilityData { ActionPointCost = 2 });
    data.Capabilities.Add(new TimedEffectCapabilityData
    {
      FireAfterTurns = expireAfterTurns,
      EffectRadius = effectRadius,
    });
    return data;
  }

  [TestCase(TestName = "State exposes data name and finds capabilities")]
  public void StateExposesDataAndCapabilities()
  {
    var state = new BattleObjectState(-1, MakeBombData(), new Vector3I(0, 0, 0));

    Assert.Equal("Alien Bomb", state.Name);
    Assert.Equal(-1, state.Id);
    Assert.True(state.Status.IsNone);

    Option<InteractiveCapability> interactive = state.FindCapability<InteractiveCapability>();
    Assert.True(interactive.IsSome);
    Assert.Equal(2, interactive.RequireSome().ActionPointCost);
  }

  [TestCase(TestName = "Duplicate capability kinds are rejected at construction")]
  public void DuplicateCapabilityThrows()
  {
    var data = new BattleSpecialObjectData { Name = "Broken" };
    data.Capabilities.Add(new InteractiveCapabilityData());
    data.Capabilities.Add(new InteractiveCapabilityData());

    Assert.Throws<System.InvalidOperationException>(
      () => new BattleObjectState(-1, data, new Vector3I(0, 0, 0)));
  }

  [TestCase(TestName = "Timed effect runtime clamps non-positive radius and copies effects")]
  public void ElapsedTriggerRuntimeShape()
  {
    var data = new TimedEffectCapabilityData
    {
      FireAfterTurns = 3,
      EffectRadius = 0,
    };
    data.Effects.Add(new DamageEffectData { BaseDamage = 8 });

    var runtime = new TimedEffectCapability(data);
    Assert.Equal(3, runtime.ExpireAfterTurns);
    Assert.Equal(0, runtime.EffectRadius);
    Assert.Equal(1, runtime.Effects.Count);
  }
}
