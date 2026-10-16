using FunProject.Battle;
using FunProject.Currencies;
using System.Collections.Generic;
using FunProject.Geoscape;
using FunProject.Weapons;
using Godot;
using GdUnit4;
using System.Threading.Tasks;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MissionResultsViewTest
{
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  [TestCase(1280, 960)]
  public static async Task TallyUsesFinalBattleCountsAfterRuntimeDisposal(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Duel(playerControlled: true);
    var fallen = battle.Spawn(MakeCombatant("Fallen", battle.PlayerFaction), new Vector3I(0, 0, 0));
    var unconscious = battle.Spawn(MakeCombatant("Unconscious", battle.PlayerFaction), new Vector3I(1, 0, 0));
    var extraEnemy = battle.Spawn(MakeCombatant("Extra enemy", battle.EnemyFaction), new Vector3I(2, 0, 0));
    var captive = battle.Spawn(MakeCombatant("Captive", battle.EnemyFaction), new Vector3I(3, 0, 0));
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.ApplyDamage(battle.PlayerUnit, 1);
    battle.ApplyDamage(fallen, 999);
    battle.ApplyDamage(unconscious, 999, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 999);
    battle.ApplyDamage(extraEnemy, 999);
    battle.ApplyDamage(captive, 999, DamageKind.Stun);
    var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    var result = battle.Query(new GetBattleResultQuery()).RequireRight();
    Assert.Equal(0, summary.DefeatedPerCombatant.Count, "These enemy deaths have no soldier kill credit.");
    battle.Runtime.Dispose();

    var view = GD.Load<PackedScene>("res://scenes/geoscape/results/MissionResultsView.tscn")
      .Instantiate<MissionResultsView>();
    var window = ((SceneTree)Engine.GetMainLoop()).Root;
    Vector2I originalSize = window.Size;
    window.Size = new Vector2I(width, height);
    AddToTree(view);
    try
    {
      view.Bind("Bomb Defusal", summary, result, new Dictionary<Currency, ulong> { [MakeCurrency("Credits")] = 321 });
      await WaitForLayout(view);

      Assert.True(view.GetNode<Label>("%Rewards").Text.Contains("+321 Credits", System.StringComparison.Ordinal));
      Assert.Equal("Bomb Defusal", view.GetNode<Label>("%MissionType").Text);
      Assert.Equal("2", view.GetNode<Label>("%TurnsTaken").Text);
      Assert.Equal("1", view.GetNode<Label>("%SoldiersWounded").Text);
      Assert.Equal("1", view.GetNode<Label>("%SoldiersKilled").Text);
      Assert.Equal("2", view.GetNode<Label>("%EnemiesKilled").Text);
      Rect2 screen = ScreenRect(view);
      foreach (string name in new[] { "MissionType", "TurnsTaken", "SoldiersWounded", "SoldiersKilled", "EnemiesKilled", "Rewards", "ContinueButton" })
        Assert.True(screen.Encloses(ScreenRect(view.GetNode<Control>($"%{name}"))), $"{name} must fit the results screen.");

      // Exercise the physical window's actual GUI dispatch after native canvas scaling.
      int continued = 0;
      view.BackRequested += () => continued++;
      await window.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
      string evidence = OS.GetEnvironment("UI_LAYOUT_EVIDENCE");
      if (!string.IsNullOrEmpty(evidence))
      {
        using var pixels = window.GetTexture().GetImage();
        Assert.Equal(new Vector2I(width, height), pixels.GetSize());
        pixels.SavePng($"{evidence}/mission-results-{width}x{height}.png");
      }
      Vector2 pointer = ScreenRect(view.GetNode<Control>("%ContinueButton")).GetCenter();
      window.NotifyMouseEntered();
      window.PushInput(new InputEventMouseMotion { Position = pointer, GlobalPosition = pointer }, true);
      foreach (bool pressed in new[] { true, false })
        window.PushInput(new InputEventMouseButton
        {
          Position = pointer,
          GlobalPosition = pointer,
          ButtonIndex = MouseButton.Left,
          Pressed = pressed,
        }, true);
      Assert.Equal(1, continued);
    }
    finally
    {
      window.Size = originalSize;
      await WaitForLayout(window);
    }

  }
}
