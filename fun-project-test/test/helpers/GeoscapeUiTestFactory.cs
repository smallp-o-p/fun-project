using System;
using System.Threading.Tasks;
using FunProject.GameState;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Code-built mirrors of authored geoscape scenes for UI suites: the GdUnit runner's Godot
// instance runs with fun-project-test as its res:// root, so authored .tscn files cannot be
// loaded from here. Fixtures rebuild the node trees the controller scripts resolve (%names)
// in code and let _Ready wire them — pinning push logic. The scene files' own wiring is
// exercised by running the game.
internal static class GeoscapeUiTestFactory
{
  // Detached UnitView fixture: every %name UnitView resolves, without the authored layout.
  // The Paths action needs its SkillProgressionView child and is not exercised here.
  public static UnitView CreateUnitView()
  {
    var view = new UnitView();
    var title = new RichTextLabel { Name = "Title" };
    var statsList = new VBoxContainer { Name = "StatsList" };
    var buffsList = new VBoxContainer { Name = "BuffsList" };
    var personalModSlots = new VBoxContainer { Name = "PersonalModSlots" };
    var weaponModSlots = new VBoxContainer { Name = "WeaponModSlots" };
    var utilitySlots = new VBoxContainer { Name = "UtilitySlots" };
    var armoryList = new VBoxContainer { Name = "ArmoryList" };
    var backButton = new Button { Name = "BackButton" };
    var unequipButton = new Button { Name = "UnequipButton" };
    var weaponSlot = new Button { Name = "WeaponSlot" };
    var armorSlot = new Button { Name = "ArmorSlot" };
    var pathsButton = new Button { Name = "PathsButton" };

    Node[] owned = [title, statsList, buffsList, personalModSlots, weaponModSlots,
      utilitySlots, armoryList, backButton, unequipButton, weaponSlot, armorSlot, pathsButton];
    foreach (Node node in owned)
    {
      node.UniqueNameInOwner = true;
      view.AddChild(node);
      node.Owner = view;
    }

    return view;
  }

  public static EngineeringView CreateEngineeringView()
  {
    var view = new EngineeringView { Name = "EngineeringView" };
    var margin = Add(view, new MarginContainer { Name = "Margin" });
    var layout = Add(margin, new VBoxContainer { Name = "Layout" });
    var header = Add(layout, new HBoxContainer { Name = "Header" });
    Add(header, new Label { Name = "Title", Text = "Engineering" });
    Add(header, new Button { Name = "BackButton", Text = "Back" }, true);
    Add(layout, new Label { Name = "PauseHint", Text = "Projects advance when you return to the map." });
    Add(layout, new Label { Name = "ActiveJob" }, true);
    var body = Add(layout, new HBoxContainer { Name = "Body" });
    var listScroll = Add(body, new ScrollContainer { Name = "ItemsScroll" });
    Add(listScroll, new VBoxContainer { Name = "ManufacturableItems" }, true);
    var details = Add(body, new VBoxContainer { Name = "Details" });
    var detailsScroll = Add(details, new ScrollContainer
    {
      Name = "DetailsScroll",
      SizeFlagsVertical = Control.SizeFlags.ExpandFill,
      HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
    });
    var text = Add(detailsScroll, new VBoxContainer { Name = "Text", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
    text.AddThemeConstantOverride("separation", 12);
    string[] fields = ["ItemName", "ItemDescription", "ManufacturingDuration", "Stock", "SupplyEffect"];
    foreach (string field in fields)
      Add(text, new Label { Name = field }, true);
    Add(details, new Button
    {
      Name = "ManufactureButton",
      Text = "Manufacture",
      Disabled = true,
    }, true);
    Add(details, new Label { Name = "Status" }, true);
    ConfigureProjectLayout(view);
    return view;

    T Add<T>(Node parent, T node, bool unique = false) where T : Node
    {
      parent.AddChild(node);
      node.Owner = view;
      node.UniqueNameInOwner = unique;
      return node;
    }
  }

  // Mirror authored layout constraints as well as ancestry for real frame/layout tests.
  private static void ConfigureProjectLayout(Control view)
  {
    view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    var margin = view.GetNode<MarginContainer>("Margin");
    string[] sides = ["left", "top", "right", "bottom"];
    foreach (string side in sides)
      margin.AddThemeConstantOverride($"margin_{side}", 24);
    view.GetNode<VBoxContainer>("Margin/Layout").AddThemeConstantOverride("separation", 12);
    view.GetNode<Label>("Margin/Layout/Header/Title").SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    view.GetNode<Button>("%BackButton").CustomMinimumSize = new Vector2(120, 0);
    var body = view.GetNode<HBoxContainer>("Margin/Layout/Body");
    body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    body.AddThemeConstantOverride("separation", 24);
    var listScroll = body.GetNode<ScrollContainer>("ItemsScroll");
    listScroll.CustomMinimumSize = new Vector2(260, 180);
    listScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    listScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    listScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
    var list = listScroll.GetChild<VBoxContainer>(0);
    list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    list.AddThemeConstantOverride("separation", 8);
    var details = body.GetNode<VBoxContainer>("Details");
    details.CustomMinimumSize = new Vector2(340, 0);
    details.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    details.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    details.AddThemeConstantOverride("separation", 12);
    view.GetNode<Button>("%ManufactureButton").CustomMinimumSize = new Vector2(180, 0);
    foreach (Node node in view.FindChildren("*", "Label"))
      if (node.Name != "Title")
        ((Label)node).AutowrapMode = TextServer.AutowrapMode.WordSmart;
  }

  public static SubViewport CreateUiViewport(Control content, Vector2I size)
  {
    var viewport = new SubViewport { Size = size };
    viewport.AddChild(content);
    return AddToTree(viewport);
  }

  public static async Task WaitForLayout(Node node)
  {
    for (int i = 0; i < 10; i++)
      await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  public static Rect2 ScreenRect(Control control)
    => control.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, control.Size);

  public static GeoscapeHud CreateHud()
  {
    var hud = new GeoscapeHud { Name = "GeoscapeHud" };
    var topBar = Add(hud, new HBoxContainer { Name = "TopBar" });
    Add(topBar, new Label { Name = "ClockLabel", Text = "..." }, true);
    var timeButtons = Add(topBar, new HBoxContainer { Name = "TimeButtons" });
    Add(timeButtons, new Button
    {
      Name = "PauseButton",
      Text = "Pause",
      ToggleMode = true,
      ButtonPressed = true,
    }, true);
    Add(timeButtons, new Button { Name = "SpeedButton", Text = "1x" }, true);
    Add(topBar, new Button { Name = "UnitsButton", Text = "Units" }, true);
    Add(topBar, new Button { Name = "EngineeringButton", Text = "Engineering" }, true);
    Add(hud, new VBoxContainer { Name = "Alerts" }, true);
    var projects = Add(hud, new PanelContainer { Name = "Projects" });
    var margin = Add(projects, new MarginContainer { Name = "Margin" });
    var status = Add(margin, new VBoxContainer { Name = "Status" });
    Add(status, new Label { Name = "EngineeringProgress" }, true);
    Add(status, new Label { Name = "EngineeringNotice" }, true);
    return hud;

    T Add<T>(Node parent, T node, bool unique = false) where T : Node
    {
      parent.AddChild(node);
      node.Owner = hud;
      node.UniqueNameInOwner = unique;
      return node;
    }
  }

  public static GeoscapeViewManager CreateProjectViewManager()
  {
    var manager = new GeoscapeViewManager
    {
      Name = "ViewManager",
      EngineeringView = Pack(CreateEngineeringView()),
    };
    manager.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    return manager;
  }

  public static GeoscapeScene CreateGeoscapeScene(CampaignStartData start)
  {
    var scene = new GeoscapeScene { Start = start };
    scene.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    var marker = new RegionButton();
    var fill = new Polygon2D
    {
      Name = "Fill",
      Polygon = [new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 10)],
    };
    marker.AddChild(fill);
    fill.Owner = marker;
    var map = new GeoscapeMapControl { Name = "Map", EventMarkerScene = Pack(marker) };
    var resolution = new GeoscapeEventResolution { Name = "ResolutionDialog" };
    Node[] resolutionNodes = [new Label { Name = "Title" }, new Label { Name = "Description" },
      new HBoxContainer { Name = "Buttons" }];
    foreach (Node node in resolutionNodes)
    {
      resolution.AddChild(node);
      node.Owner = resolution;
      node.UniqueNameInOwner = true;
    }

    var viewLayer = new CanvasLayer { Name = "ViewLayer", Layer = 5 };
    var resolutionLayer = new CanvasLayer { Name = "ResolutionLayer", Layer = 10 };
    Node[] children = [map, CreateHud(), viewLayer, resolutionLayer];
    foreach (Node child in children)
      AddOwned(scene, child);
    AddOwned(viewLayer, CreateProjectViewManager());
    AddOwned(resolutionLayer, resolution);
    return scene;

    // Internal unique nodes keep their nested scene owner; the child roots remain
    // owned by GeoscapeScene even through a CanvasLayer, matching authored lookup.
    void AddOwned(Node parent, Node child)
    {
      parent.AddChild(child);
      child.Owner = scene;
      child.UniqueNameInOwner = true;
    }
  }

  public static PackedScene Pack(Node prototype)
  {
    var packed = new PackedScene();
    Error error = packed.Pack(prototype);
    prototype.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    return packed;
  }

  // The manager also hosts backdrop infrastructure; require exactly one matching view.
  public static T OnlyChild<T>(Node parent) where T : Node
    => parent.GetChildren().AsValueEnumerable().OfType<T>().Single();

  // Enter the tree under the GdUnit scene root (tree entry stands in for scene load, so
  // _Ready wires the %nodes); AutoFree releases it after the suite. Children free transitively.
  public static T AddToTree<T>(T node) where T : Node
  {
    AutoFree(node);
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
    return node;
  }
}
