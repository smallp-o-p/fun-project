using System.Threading.Tasks;
using FunProject.Items.Capabilities;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class UnitViewLayoutTest
{
  [TestCase]
  public async Task OpeningUnitSelectsWeaponAndShowsItsArmoryImmediately()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var rifle = MakeWeaponData(name: "Rifle");
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()], armory: [rifle, MakeItemData("Kit")]));
    var view = AddToTree(CreateUnitView());

    view.Present(campaign.State, campaign.State.Roster[0]);

    var choices = view.GetNode<VBoxContainer>("%ArmoryList");
    Assert.Equal(1, choices.GetChildCount());
    Assert.True(choices.GetChild<Button>(0).Text.Contains("Rifle"));
    Assert.True(view.GetNode<Button>("%WeaponSlot").ButtonPressed);
  }

  [TestCase]
  public async Task FifthUtilitySlotEquipsImmediatelyAndRetainsDetailsAfterRefresh()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Field Kit");
    item.Description = "A useful field kit.";
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()], armory: [item]));
    var unit = campaign.State.Roster[0];
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, unit);
    var slots = view.GetNode<VBoxContainer>("%UtilitySlots");
    Assert.Equal(5, slots.GetChildCount());

    slots.GetChild<Button>(4).EmitSignal(Button.SignalName.Pressed);
    ArmoryButton(view, "Field Kit").EmitSignal(Button.SignalName.Pressed);
    view.Present(campaign.State, campaign.Session);

    Assert.Equal("Field Kit", unit.Inventory[4].ItemName);
    Assert.Equal("Field Kit", view.GetNode<Label>("%SelectedItemName").Text);
    Assert.True(view.GetNode<RichTextLabel>("%SelectedItemDetails").Text.Contains(item.Description));
    Assert.True(slots.GetChild<Button>(4).ButtonPressed);
    view.GetNode<Button>("%UnequipButton").EmitSignal(Button.SignalName.Pressed);
    Assert.False(unit.Inventory.ContainsKey(4));
    Assert.Equal(1, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());
    Assert.Equal("Empty slot", view.GetNode<Label>("%SelectedItemName").Text);
  }

  [TestCase]
  public async Task UnequippingWeaponClearsItsDetailsTooltip()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var rifle = MakeWeaponData(name: "Rifle");
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()], armory: [rifle]));
    var view = AddToTree(CreateUnitView());
    view.Present(campaign.State, campaign.State.Roster[0]);
    ArmoryButton(view, "Rifle").EmitSignal(Button.SignalName.Pressed);
    Assert.True(view.GetNode<Button>("%WeaponSlot").TooltipText.Contains("Rifle"));

    view.GetNode<Button>("%UnequipButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("", view.GetNode<Button>("%WeaponSlot").TooltipText);
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task BothModSlotKindsEquipAndUnequipFromTheActiveInspector(bool weaponMod)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var rifle = MakeWeaponData(name: "Rifle");
    rifle.Capabilities.Add(new ModSlotsCapabilityData { SlotCount = 2 });
    var mod = MakeMod("Reflex chip");
    mod.Description = "Improves reflexes.";
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry()], armory: [rifle], modStock: [mod]));
    var view = AddToTree(CreateUnitView());
    var unit = campaign.State.Roster[0];
    view.Present(campaign.State, unit);
    ArmoryButton(view, "Rifle").EmitSignal(Button.SignalName.Pressed);
    var slots = view.GetNode<VBoxContainer>(weaponMod ? "%WeaponModSlots" : "%PersonalModSlots");
    slots.GetChild<Button>(1).EmitSignal(Button.SignalName.Pressed);

    ArmoryButton(view, mod.Name).EmitSignal(Button.SignalName.Pressed);

    var target = weaponMod ? unit.EquippedWeapon.RequireSome().GetModSlots()[1] : unit.GetModSlots()[1];
    Assert.True(ReferenceEquals(mod, target.EquippedMod.RequireSome()));
    Assert.Equal(mod.Name, view.GetNode<Label>("%SelectedItemName").Text);
    Assert.Equal(mod.Description, view.GetNode<RichTextLabel>("%SelectedItemDetails").Text);
    Assert.True(slots.GetChild<Button>(1).ButtonPressed);
    view.GetNode<Button>("%UnequipButton").EmitSignal(Button.SignalName.Pressed);
    Assert.False(target.HasMod);
    Assert.Equal(1, view.GetNode<VBoxContainer>("%ArmoryList").GetChildCount());
  }

  [TestCase]
  public async Task CompactViewportScrollsToTheFifthUtilitySlotOnKeyboardFocus()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var rifle = MakeWeaponData(name: "Rifle");
    rifle.Capabilities.Add(new ModSlotsCapabilityData { SlotCount = 2 });
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()], armory: [rifle]));
    var view = CreateUnitView();
    CreateUiViewport(view, new Vector2I(800, 600));
    view.Present(campaign.State, campaign.State.Roster[0]);
    ArmoryButton(view, "Rifle").EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(view);
    var finalUtility = view.GetNode<VBoxContainer>("%UtilitySlots").GetChild<Button>(4);

    finalUtility.GrabFocus();
    await WaitForLayout(view);

    var scroll = view.GetNode<ScrollContainer>("%DetailsScroll");
    Assert.True(ScreenRect(scroll).Encloses(ScreenRect(finalUtility)));
    Assert.True(scroll.ScrollVertical > 0);
    Assert.True(ScreenRect(view).Encloses(ScreenRect(view.GetNode<Button>("%PathsButton"))));
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task LongSkillPathsScrollWhileNavigationAndPointsRemainVisible(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var unit = campaign.State.Roster[0];
    var path = MakePath("Long path");
    for (int i = 0; i < 40; i++)
      path.Steps.Add(MakeStep(1));
    unit.Progression.TryCommit(path);
    var view = GD.Load<PackedScene>("res://scenes/geoscape/units/SkillProgressionView.tscn")
      .Instantiate<SkillProgressionView>();
    view.Unit = unit;
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(campaign.State, campaign.Session);
    await WaitForLayout(view);
    var bounds = new Rect2(0, 0, width, height);

    foreach (string node in new[] { "%CloseButton", "%CurrencyLabel", "%PathsScroll" })
      Assert.True(bounds.Encloses(ScreenRect(view.GetNode<Control>(node))));
    var scroll = view.GetNode<ScrollContainer>("%PathsScroll");
    Assert.True(scroll.GetVScrollBar().MaxValue > scroll.Size.Y);
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task UnitLayoutKeepsIdentityAndActionsInsideViewport(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var view = CreateUnitView();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(campaign.State, campaign.State.Roster[0]);
    await WaitForLayout(view);
    var bounds = new Rect2(0, 0, width, height);

    foreach (string path in new[] { "%BackButton", "%PathsButton", "%Title", "%DetailsScroll", "%UnitPresentation" })
      Assert.True(bounds.Encloses(ScreenRect(view.GetNode<Control>(path))));
    Assert.True(view.GetNode<ScrollContainer>("%DetailsScroll").Size.Y > 200);
    Assert.True(view.GetNode<Control>("%UnitPresentation").Size.X >= width * 0.3f);
    Assert.Equal(6, view.GetNode<Container>("%StatsList").GetChildCount());
  }

  [TestCase(800, 600)]
  [TestCase(1066, 600)] // Logical canvas for 16:9, including 4K with native UI scaling.
  [TestCase(1433, 600)] // Logical canvas for 3440x1440 ultrawide.
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  [TestCase(3440, 1440)]
  [TestCase(3840, 2160)]
  public async Task EntireUnitBodyKeepsPaddingThroughViewportResizes(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var view = CreateUnitView();
    var viewport = CreateUiViewport(view, new Vector2I(width, height));
    view.Present(campaign.State, campaign.State.Roster[0]);

    foreach (var size in new[] { new Vector2I(width, height), new Vector2I(800, 600), new Vector2I(width, height) })
    {
      viewport.Size = size;
      await WaitForLayout(view);
      AssertFullBodyHasPadding(view.GetNode<SubViewportContainer>("%UnitPresentation"));
    }
  }

  private static void AssertFullBodyHasPadding(SubViewportContainer presentation)
  {
    var modelViewport = presentation.GetNode<SubViewport>("%ModelViewport");
    var camera = presentation.GetNode<Camera3D>("%ModelCamera");
    var meshes = presentation.GetNode<Node3D>("%ModelRoot")
      .FindChildren("*", "MeshInstance3D", true, false)
      .AsValueEnumerable().Cast<MeshInstance3D>()
      .Where(mesh => mesh.Mesh is not null && mesh.IsVisibleInTree()).ToArray();
    Assert.True(meshes.Length > 0, "The authored unit must have visible model geometry.");
    var size = (Vector2)modelViewport.Size;
    var paddedFrame = new Rect2(size * 0.02f, size * 0.96f);

    // Check projected geometry rather than exported framing values: head, feet, and
    // wide limbs must all remain inside the actual render viewport after every resize.
    foreach (var mesh in meshes)
    {
      Aabb bounds = mesh.GetAabb();
      for (int corner = 0; corner < 8; corner++)
      {
        Vector3 worldPoint = mesh.GlobalTransform * bounds.GetEndpoint(corner);
        Vector2 screenPoint = camera.UnprojectPosition(worldPoint);
        Assert.True(camera.IsPositionInFrustum(worldPoint),
          $"{mesh.Name} corner {corner} must remain inside the camera frustum at {size}.");
        Assert.True(paddedFrame.HasPoint(screenPoint),
          $"{mesh.Name} corner {corner} at {screenPoint} must have 2% padding inside {size}.");
      }
    }
  }
}
