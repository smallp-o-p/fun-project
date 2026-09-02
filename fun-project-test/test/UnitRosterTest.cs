using FunProject.Combatants;
using FunProject.GameState;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestFactory;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class UnitRosterTest
{
  // Rebuild-in-code pattern (GeoscapeHudTest precedent): the GdUnit root is the test
  // subproject, so the authored UnitLabel.tscn cannot be loaded; we pack one in code.
  private static UnitLabel BuildLabelProto()
  {
    var label = new UnitLabel();
    var hbox = new HBoxContainer { Name = "HBoxContainer" };
    var unitIcon = new TextureRect { Name = "UnitIcon" };
    var name = new RichTextLabel { Name = "Name" };
    var rankIcon = new TextureRect { Name = "RankIcon" };
    var rank = new RichTextLabel { Name = "RankName" };
    var status = new RichTextLabel { Name = "Status" };
    foreach (Node node in new Node[] { unitIcon, name, rankIcon, rank, status })
      node.UniqueNameInOwner = true;
    hbox.AddChild(unitIcon);
    hbox.AddChild(name);
    hbox.AddChild(rankIcon);
    hbox.AddChild(rank);
    hbox.AddChild(status);
    label.AddChild(hbox);
    hbox.Owner = label;
    foreach (Node child in hbox.GetChildren())
      child.Owner = label;
    return label;
  }

  private static UnitRoster BuildRoster()
  {
    var labelScene = new PackedScene();
    var labelProto = BuildLabelProto();
    labelScene.Pack(labelProto);
    labelProto.Free();

    var roster = AutoFree(new UnitRoster { UnitLabelScene = labelScene });

    var margin = new MarginContainer { Name = "MarginContainer" };
    var vbox = new VBoxContainer { Name = "VBoxContainer" };
    var header = new HBoxContainer { Name = "Header" };
    var back = new Button { Name = "BackButton" };
    back.UniqueNameInOwner = true;
    var labels = new VBoxContainer { Name = "UnitLabels" };
    labels.UniqueNameInOwner = true;

    header.AddChild(back);
    vbox.AddChild(header);
    vbox.AddChild(labels);
    margin.AddChild(vbox);
    roster.AddChild(margin);
    back.Owner = roster;
    labels.Owner = roster;

    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(roster); // enter tree -> _Ready wires %nodes
    return roster;
  }

  private static FunProject.GameState.GameState MakeState(int entryCount)
  {
    RosterEntryData[] entries = new RosterEntryData[entryCount];
    for (int i = 0; i < entryCount; i++)
      entries[i] = MakeEntry($"Soldier {i + 1}");
    return new GameState(MakeStart(roster: entries));
  }

  [TestCase(TestName = "Present builds one row per roster entry with bound names")]
  public void PresentBuildsRows()
  {
    var roster = BuildRoster();

    roster.Present(MakeState(3));

    var labels = roster.GetNode<VBoxContainer>("%UnitLabels");
    Assert.Equal(3, labels.GetChildCount());
    Assert.Equal("Soldier 1", labels.GetChild(0).GetNode<RichTextLabel>("%Name").Text);
    Assert.Equal("Soldier 3", labels.GetChild(2).GetNode<RichTextLabel>("%Name").Text);
  }

  [TestCase(TestName = "Rows show rank/status placeholders")]
  public void RowsShowPlaceholders()
  {
    var roster = BuildRoster();

    roster.Present(MakeState(1));

    var row = roster.GetNode<VBoxContainer>("%UnitLabels").GetChild(0);
    Assert.Equal("—", row.GetNode<RichTextLabel>("%RankName").Text);
    Assert.Equal("Ready", row.GetNode<RichTextLabel>("%Status").Text);
  }

  [TestCase(TestName = "Present again rebuilds rows from scratch (no stale rows)")]
  public void PresentRebuilds()
  {
    var roster = BuildRoster();

    roster.Present(MakeState(2));
    roster.Present(MakeState(4));

    Assert.Equal(4, roster.GetNode<VBoxContainer>("%UnitLabels").GetChildCount());
  }

  [TestCase(TestName = "Back button invokes the armed close request")]
  public void BackButtonInvokesArmedCloseRequest()
  {
    var roster = BuildRoster();
    bool requested = false;
    roster.ArmClose(() => requested = true);

    roster.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);

    Assert.True(requested);
  }

  [TestCase(TestName = "Row press raises UnitSelected with the bound combatant")]
  public void RowPressRaisesUnitSelected()
  {
    var roster = BuildRoster();
    Combatant? selected = null;
    roster.UnitSelected += unit => selected = unit;

    roster.Present(MakeState(2));
    var row = (UnitLabel)roster.GetNode<VBoxContainer>("%UnitLabels").GetChild(0);
    row.Press();

    Assert.True(selected is not null);
    Assert.Equal("Soldier 1", selected!.Name);
  }

  [TestCase(TestName = "Ready makes every descendant control ignore mouse input")]
  public void DescendantControlsIgnoreMouseInput()
  {
    var label = AutoFree(BuildLabelProto());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(label);

    foreach (Node node in label.FindChildren("*", "Control"))
      Assert.Equal(Control.MouseFilterEnum.Ignore, ((Control)node).MouseFilter);
  }

  [TestCase(TestName = "Bind sets the combatant name (UnitLabel unit test)")]
  public void BindSetsName()
  {
    var label = AutoFree(BuildLabelProto());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(label);

    label.Bind(new Combatant(
      MakeCombatantData("Trooper"), new Faction(new FactionData()), Some("Cpl. Ada Voss")));

    Assert.Equal("Cpl. Ada Voss", label.GetNode<RichTextLabel>("%Name").Text);
  }
}
