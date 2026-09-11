using System.Threading.Tasks;
using FunProject.GameState;
using FunProject.Items;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeUnitNavigationTest
{
  private static GeoscapeScene BuildScene(
    RosterEntryData[] roster, EquippableItemData[] armory)
    => AddToTree(CreateGeoscapeScene(MakeStart(roster: roster, armory: armory)));

  private static GeoscapeViewManager Manager(GeoscapeScene scene)
    => scene.GetNode<GeoscapeViewManager>("%ViewManager");

  private static void PressUnitsButton(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Button>("%UnitsButton")
      .EmitSignal(Button.SignalName.Pressed);

  [TestCase]
  public async Task UnitsButtonOpensTheRosterWithRowsOverTheHiddenMap()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = BuildScene([MakeEntry(), MakeEntry()], []);
    var manager = Manager(scene);

    PressUnitsButton(scene);

    var roster = (UnitRoster)manager.Current;
    var rows = roster.GetNode<VBoxContainer>("%UnitLabels");
    Assert.Equal(2, rows.GetChildCount()); // placeholder rows cleared, roster rows built
    Assert.Equal("Mold", rows.GetChild<UnitLabel>(0).UnitName!.Text);
    Assert.False(MapViewport(scene).IsVisibleInTree());
    Assert.False(scene.GetNode<GeoscapeHud>("%GeoscapeHud").IsVisibleInTree());
  }

  [TestCase]
  public async Task RowClickOpensABoundUnitViewOverTheRetainedRoster()
  {
    var scene = BuildScene([MakeEntry()], []);
    var manager = Manager(scene);
    PressUnitsButton(scene);
    var roster = (UnitRoster)manager.Current;
    var row = roster.GetNode<VBoxContainer>("%UnitLabels").GetChild<UnitLabel>(0);

    row.Press();

    var unit = (UnitView)manager.Current;
    Assert.Equal("Mold", unit.GetNode<RichTextLabel>("%Title").Text);
    Assert.False(roster.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Disabled, roster.ProcessMode);
    Assert.False(MapViewport(scene).IsVisibleInTree());

    unit.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(roster, manager.Current));
    Assert.True(roster.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Inherit, roster.ProcessMode);
    Assert.True(unit.GetParent() is null);
    Assert.True(unit.IsQueuedForDeletion());
    await WaitForDeferredDeletion(roster.GetTree()); // unit is queued for deletion
    Assert.False(GodotObject.IsInstanceValid(unit));
  }

  [TestCase]
  public async Task PathsAwardCommitAndCloseRefreshAimWhileKeepingSlotSelection()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = BuildScene(
      [MakeEntry(unit: MakeCombatantData("Aimer"))],
      [MakeItemData("Kit A"), MakeItemData("Kit B")]);
    var manager = Manager(scene);
    PressUnitsButton(scene);
    var roster = (UnitRoster)manager.Current;
    roster.GetNode<VBoxContainer>("%UnitLabels").GetChild<UnitLabel>(0).Press();
    var unit = (UnitView)manager.Current;
    Assert.Equal("Aimer", unit.GetNode<RichTextLabel>("%Title").Text);

    unit.SelectSlot(UnitViewSlot.Utility(0));
    Assert.Equal(2, unit.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());

    unit.GetNode<Button>("%PathsButton").EmitSignal(Button.SignalName.Pressed);

    var paths = (SkillProgressionView)manager.Current;
    Assert.Equal("res://scenes/geoscape/units/SkillProgressionView.tscn", paths.SceneFilePath);
    Assert.Equal("Aimer — Skill Paths", paths.GetNode<RichTextLabel>("%Title").Text);
    Assert.False(unit.IsVisibleInTree());

    paths.GetNode<Button>("%AwardButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Points: 5", paths.GetNode<RichTextLabel>("%CurrencyLabel").Text);

    // Commit the authored Marksman path by name (the catalog also lists Guardian), then
    // buy its first step (cost 1): 5 points leave 4, and the +5 Aim mod raises 65 to 70.
    // Later steps (costs 2 and 3) remain — the chain is intentionally left incomplete.
    CommitButton(paths, "Marksman").EmitSignal(Button.SignalName.Pressed);
    OnlyChild<Button>(paths.GetNode<VBoxContainer>("%CommittedList"))
      .EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Points: 4", paths.GetNode<RichTextLabel>("%CurrencyLabel").Text);

    // The real Close button pops; the composition root re-presents the same UnitView.
    paths.GetNode<Button>("%CloseButton").EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(unit, manager.Current));
    Assert.True(unit.IsVisibleInTree());
    Assert.True(StatsText(unit).Contains("Aim: 65 -> 70"));
    Assert.Equal(2, unit.GetNode<VBoxContainer>("%ArmoryList").GetChildCount()); // slot retained
    Assert.True(paths.GetParent() is null && paths.IsQueuedForDeletion());
  }

  private static Button CommitButton(SkillProgressionView paths, string pathName)
    => paths.GetNode<VBoxContainer>("%AvailableList").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains(pathName));

  private static string StatsText(UnitView unit)
  {
    var text = "";
    foreach (Node row in unit.GetNode<VBoxContainer>("%StatsList").GetChildren())
      if (row is RichTextLabel label)
        text += $"{label.Text}\n";
    return text;
  }
}
