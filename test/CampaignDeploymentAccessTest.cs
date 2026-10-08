using System.Threading.Tasks;
using FunProject.Battle;
using FunProject.Strategic;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public sealed class CampaignDeploymentAccessTest
{
  [TestCase]
  public async Task DeployedEditRejectsBeforeArmoryWithdrawal()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var rifle = MakeWeaponData(name: "Rifle");
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()], armory: [rifle]));
    var unit = campaign.State.Roster[0];
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, unit);
    var button = view.GetNode<VBoxContainer>("%ArmoryList").GetChild<Button>(0);
    using var battle = BattleFixture.Started(new Vector3I(3, 1, 1), new UnitPlacement(new UnitLoadout(unit), Vector3I.Zero));
    int before = campaign.State.Armory.ItemStock()[0].Remaining;
    button.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(before, campaign.State.Armory.ItemStock()[0].Remaining);
    battle.Read(() => Assert.True(unit.EquippedWeapon.IsNone));
    view.Present(campaign.State, unit);
  }

  [TestCase]
  public void ClosingMissionRejectsDuplicateLaunch()
  {
    var mission = MakeTacticalMission(MakeTacticalBattleType(), maxPlayerUnits: 1, minEnemyUnits: 1, maxEnemyUnits: 1);
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()], timeline:
      [MakeScheduled(1, MakeEvent("Mission", GeoscapeEventKind.TacticalBattle, tacticalMission: mission))]));
    campaign.AdvanceTicks(1);
    var active = campaign.ActiveEvent; campaign.OpenResolution(active);
    var first = campaign.Session.LaunchMission(active, [campaign.State.Roster[0]]).RequireRight();
    try
    {
      using (BattleConcurrencyProbe.HoldQuery(first.Runtime))
      {
        first.Runtime.RequestClose();
        Assert.True(campaign.Session.ActiveMission.IsSome);
        var duplicate = campaign.Session.LaunchMission(active, [campaign.State.Roster[0]]).RequireLeft();
        Assert.Equal(BattleSetupFailureReason.MissionAlreadyActive, duplicate.Reason);
      }
    }
    finally { first.Runtime.Dispose(); }
  }
}
