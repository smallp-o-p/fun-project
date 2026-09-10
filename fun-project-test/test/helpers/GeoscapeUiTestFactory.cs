using System;
using System.Threading.Tasks;
using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Progression;
using FunProject.Stats;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Code-built mirrors of authored geoscape scenes for UI suites: the GdUnit runner's Godot
// instance runs with fun-project-test as its res:// root, so authored .tscn files cannot be
// loaded from here. Fixtures rebuild the node trees the controller scripts resolve (%names)
// in code and let _Ready wire them — pinning push logic. The scene files' own wiring is
// exercised by running the game.
internal static partial class GeoscapeUiTestFactory
{
  private static SkillPathCatalogData? _skillCatalog;

  // The authored skill catalog lives under the main project's res://, unreachable from the
  // test runtime's res:// root. One in-memory SkillPathCatalogData takes over that path for
  // the whole test-runtime lifetime (production SkillPathCatalog caches on first read), so
  // the real SkillProgressionView rebuild runs against deterministic authored-type data.
  // The static reference is load-bearing: the engine cache does not keep a takeover
  // resource alive, so without it the catalog would be collected between installs and use.
  private static void InstallTestSkillCatalog()
  {
    if (_skillCatalog is not null)
      return;
    var aimMod = new AimStatMod();
    aimMod.AddModifier(StatModifier.Add(5));
    _skillCatalog = new SkillPathCatalogData();
    _skillCatalog.Paths.Add(MakePath("Aim Training", MakeStep(5, MakeStatModEffect(aimMod))));
    _skillCatalog.TakeOverPath("res://resources/skills/catalog.tres");
  }

  // Detached UnitView fixture mirroring the authored Control/Content layout: every %name
  // UnitView resolves. The Paths action uses the packed real view through its export.
  public static UnitView CreateUnitView()
  {
    var view = new UnitView { SkillProgressionViewScene = Pack(CreateSkillProgressionView()) };
    var backButton = new Button { Name = "BackButton" };
    ConfigureBaseView(view, backButton);
    var content = new PanelContainer { Name = "Content" };
    content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    view.AddChild(content); // roots first: owners are only valid for live descendants
    content.Owner = view;
    var margin = Add(content, new MarginContainer { Name = "MarginContainer" });
    var body = Add(margin, new VBoxContainer { Name = "VBoxContainer" });
    var title = new RichTextLabel { Name = "Title" };
    var statsList = new VBoxContainer { Name = "StatsList" };
    var buffsList = new VBoxContainer { Name = "BuffsList" };
    var personalModSlots = new VBoxContainer { Name = "PersonalModSlots" };
    var weaponModSlots = new VBoxContainer { Name = "WeaponModSlots" };
    var utilitySlots = new VBoxContainer { Name = "UtilitySlots" };
    var armoryList = new VBoxContainer { Name = "ArmoryList" };
    var unequipButton = new Button { Name = "UnequipButton" };
    var weaponSlot = new Button { Name = "WeaponSlot" };
    var armorSlot = new Button { Name = "ArmorSlot" };
    var pathsButton = new Button { Name = "PathsButton" };

    Node[] owned = [title, statsList, buffsList, personalModSlots, weaponModSlots,
      utilitySlots, armoryList, backButton, unequipButton, weaponSlot, armorSlot, pathsButton];
    foreach (Node node in owned)
    {
      node.UniqueNameInOwner = true;
      body.AddChild(node);
      node.Owner = view;
    }

    return view;

    T Add<T>(Node parent, T node) where T : Node
    {
      parent.AddChild(node);
      node.Owner = view;
      return node;
    }
  }

  // Real paths screen with every %name its script resolves, under a full-rect Content like
  // the authored scene. Installing the test catalog here covers every factory consumer.
  public static SkillProgressionView CreateSkillProgressionView()
  {
    InstallTestSkillCatalog();
    var view = new SkillProgressionView();
    var closeButton = new Button { Name = "CloseButton" };
    ConfigureBaseView(view, closeButton);
    var content = new PanelContainer { Name = "Content" };
    content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    view.AddChild(content); // roots first: owners are only valid for live descendants
    content.Owner = view;
    var margin = Add(content, new MarginContainer { Name = "MarginContainer" });
    var body = Add(margin, new VBoxContainer { Name = "VBoxContainer" });
    var header = Add(body, new HBoxContainer { Name = "Header" });
    Add(header, new RichTextLabel { Name = "Title" }, true);
    Add(header, closeButton, true);
    var currency = Add(body, new HBoxContainer { Name = "CurrencyBar" });
    Add(currency, new RichTextLabel { Name = "CurrencyLabel" }, true);
    Add(currency, new Button { Name = "AwardButton" }, true);
    Add(body, new VBoxContainer { Name = "CommittedList" }, true);
    Add(body, new VBoxContainer { Name = "AvailableList" }, true);
    return view;

    T Add<T>(Node parent, T node, bool unique = false) where T : Node
    {
      node.UniqueNameInOwner = unique;
      parent.AddChild(node);
      node.Owner = view;
      return node;
    }
  }

  public static UnitRoster CreateUnitRoster()
  {
    var roster = new UnitRoster
    {
      UnitLabelScene = Pack(CreateUnitLabel()),
      UnitViewScene = Pack(CreateUnitView()),
    };
    var backButton = new Button { Name = "BackButton" };
    ConfigureBaseView(roster, backButton);
    var content = new PanelContainer { Name = "Content" };
    content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    roster.AddChild(content); // roots first: owners are only valid for live descendants
    content.Owner = roster;
    var header = Add(content, new HBoxContainer { Name = "Header" });
    Add(header, new RichTextLabel { Name = "Title", Text = "Units" });
    Add(header, backButton, true);
    Add(content, new VBoxContainer { Name = "UnitLabels" }, true);
    return roster;

    T Add<T>(Node parent, T node, bool unique = false) where T : Node
    {
      node.UniqueNameInOwner = unique;
      parent.AddChild(node);
      node.Owner = roster;
      return node;
    }
  }

  // Row label proto: the authored scene's %names the UnitLabel script resolves.
  private static UnitLabel CreateUnitLabel()
  {
    var label = new UnitLabel();
    Node[] owned =
    [
      new RichTextLabel { Name = "Name" },
      new RichTextLabel { Name = "RankName" },
      new RichTextLabel { Name = "Status" },
      new TextureRect { Name = "UnitIcon" },
      new TextureRect { Name = "RankIcon" },
      new Button { Name = "ClickTarget" },
    ];
    foreach (Node node in owned)
    {
      node.UniqueNameInOwner = true;
      label.AddChild(node);
      node.Owner = label;
    }
    return label;
  }

  public static EngineeringView CreateEngineeringView()
  {
    var view = new EngineeringView { Name = "EngineeringView" };
    var backButton = new Button { Name = "BackButton", Text = "Back" };
    ConfigureBaseView(view, backButton);
    var content = new PanelContainer { Name = "Content" };
    content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    view.AddChild(content); // roots first: owners are only valid for live descendants
    content.Owner = view;
    var margin = Add(content, new MarginContainer { Name = "Margin" });
    var layout = Add(margin, new VBoxContainer { Name = "Layout" });
    var header = Add(layout, new HBoxContainer { Name = "Header" });
    Add(header, new Label { Name = "Title", Text = "Engineering" });
    Add(header, backButton, true);
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
      node.UniqueNameInOwner = unique;
      parent.AddChild(node);
      node.Owner = view;
      return node;
    }
  }

  // Mirror authored layout constraints as well as ancestry for real frame/layout tests.
  private static void ConfigureProjectLayout(Control view)
  {
    view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    var margin = view.GetNode<MarginContainer>("Content/Margin");
    string[] sides = ["left", "top", "right", "bottom"];
    foreach (string side in sides)
      margin.AddThemeConstantOverride($"margin_{side}", 24);
    view.GetNode<VBoxContainer>("Content/Margin/Layout").AddThemeConstantOverride("separation", 12);
    view.GetNode<Label>("Content/Margin/Layout/Header/Title").SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    view.GetNode<Button>("%BackButton").CustomMinimumSize = new Vector2(120, 0);
    var body = view.GetNode<HBoxContainer>("Content/Margin/Layout/Body");
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

  // Mirrors the authored GeoscapeView base scene without invoking production graph builders.
  public static void ConfigureBaseView(GeoscapeView view, BaseButton? backButton)
  {
    view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    view.BackButton = backButton;
    view.MouseFilter = Control.MouseFilterEnum.Ignore;
    var background = new SubViewportContainer
    {
      Name = "Background",
      Visible = false,
      Stretch = true,
      MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    var viewport = new SubViewport
    {
      Name = "Viewport",
      OwnWorld3D = true,
      TransparentBg = true,
    };
    view.AddChild(background);
    background.Owner = view;
    background.AddChild(viewport);
    viewport.Owner = view;
  }

  public static Node3D AddBackdrop(GeoscapeView view, Node3D backdrop)
  {
    var background = view.GetNode<SubViewportContainer>("Background");
    background.Visible = true;
    var viewport = background.GetNode<SubViewport>("Viewport");
    viewport.AddChild(backdrop);
    backdrop.Owner = view;
    return backdrop;
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

  // ProcessFrame is emitted before that frame's deferred-deletion flush. Awaiting the
  // next boundary lets QueueFree complete without relying on wall-clock timing.
  public static async Task WaitForDeferredDeletion(SceneTree tree)
  {
    await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
  }

  public static Rect2 ScreenRect(Control control)
    => control.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, control.Size);

  public static GeoscapeHud CreateHud()
  {
    var hud = new GeoscapeHud { Name = "GeoscapeHud" };
    hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    hud.MouseFilter = Control.MouseFilterEnum.Ignore;

    var topBar = Add(hud, new HBoxContainer { Name = "TopBar" }, true);
    topBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
    Add(topBar, new Label { Name = "ClockLabel", Text = "..." }, true);
    var timeButtons = Add(topBar, new HBoxContainer { Name = "TimeButtons" }, true);
    Add(timeButtons, new Button
    {
      Name = "PauseButton",
      Text = "Pause",
      ToggleMode = true,
      ButtonPressed = true,
    }, true);
    Add(timeButtons, new Button { Name = "SpeedButton", Text = "1x" }, true);
    var viewButtons = Add(topBar, new HBoxContainer { Name = "ViewButtons" }, true);
    PackedScene rosterScene = Pack(CreateUnitRoster());
    var unitsButton = Add(viewButtons, new Button
    {
      Name = "UnitsButton",
      Text = "Units",
    }, true);
    Error unitsConnection = unitsButton.Connect(Button.SignalName.Pressed,
      Callable.From(() => hud.RequestView(rosterScene)));
    if (unitsConnection != Error.Ok)
      throw new InvalidOperationException($"Units button connection failed: {unitsConnection}.");
    PackedScene engineeringScene = Pack(CreateEngineeringView());
    var engineeringButton = Add(viewButtons, new Button
    {
      Name = "EngineeringButton",
      Text = "Engineering",
    }, true);
    Error engineeringConnection = engineeringButton.Connect(Button.SignalName.Pressed,
      Callable.From(() => hud.RequestView(engineeringScene)));
    if (engineeringConnection != Error.Ok)
      throw new InvalidOperationException($"Engineering button connection failed: {engineeringConnection}.");
    Add(hud, new VBoxContainer { Name = "Alerts" }, true);
    var projects = Add(hud, new PanelContainer { Name = "Projects" });
    var margin = Add(projects, new MarginContainer { Name = "Margin" });
    var status = Add(margin, new VBoxContainer { Name = "Status" });
    Add(status, new Label { Name = "EngineeringProgress" }, true);
    Add(status, new Label { Name = "EngineeringNotice" }, true);
    return hud;

    T Add<T>(Node parent, T node, bool unique = false) where T : Node
    {
      node.UniqueNameInOwner = unique;
      parent.AddChild(node);
      node.Owner = hud;
      return node;
    }
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
    map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    map.MouseFilter = Control.MouseFilterEnum.Ignore;

    // Permanent root: interactive map viewport below, HUD sibling above — mirroring the
    // authored GeoscapeScene.tscn ViewManager/MapView assembly.
    var rootView = new GeoscapeView { Name = "MapView" };
    ConfigureBaseView(rootView, null);
    rootView.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    rootView.MouseFilter = Control.MouseFilterEnum.Ignore;
    var mapViewport = new SubViewportContainer { Name = "MapViewport", Stretch = true };
    mapViewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    var mapViewportInner = new SubViewport { Name = "Viewport", TransparentBg = true };

    var hud = CreateHud();

    var manager = new GeoscapeViewManager { Name = "ViewManager", RootView = rootView };
    manager.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    manager.MouseFilter = Control.MouseFilterEnum.Ignore;

    var resolution = new GeoscapeEventResolution { Name = "ResolutionDialog" };
    Node[] resolutionNodes = [new Label { Name = "Title" }, new Label { Name = "Description" },
      new HBoxContainer { Name = "Buttons" }];
    foreach (Node node in resolutionNodes)
    {
      node.UniqueNameInOwner = true;
      resolution.AddChild(node);
      node.Owner = resolution;
    }
    var resolutionLayer = new CanvasLayer { Name = "ResolutionLayer", Layer = 10 };

    manager.AddChild(rootView);
    rootView.AddChild(mapViewport);
    mapViewport.AddChild(mapViewportInner);
    mapViewportInner.AddChild(map);
    rootView.AddChild(hud); // after the map viewport: drawn above it, like the authored scene
    scene.AddChild(manager);
    scene.AddChild(resolutionLayer);
    resolutionLayer.AddChild(resolution);

    // Unique names stay owned by GeoscapeScene so %Map/%GeoscapeHud/%ViewManager lookups
    // match the authored scene; the HUD keeps its own nested owner.
    manager.Owner = scene;
    manager.UniqueNameInOwner = true;
    map.Owner = scene;
    map.UniqueNameInOwner = true;
    hud.Owner = scene;
    hud.UniqueNameInOwner = true;
    resolution.Owner = scene;
    resolution.UniqueNameInOwner = true;
    return scene;
  }

  // The camera is code-built by GeoscapeScene under the map's own SubViewport.
  public static GeoscapeCameraRig MapCamera(GeoscapeScene scene)
    => scene.GetNode<GeoscapeMapControl>("%Map").GetViewport().GetNode<GeoscapeCameraRig>("Camera");

  public static SubViewportContainer MapViewport(GeoscapeScene scene)
    => (SubViewportContainer)scene.GetNode<GeoscapeMapControl>("%Map").GetViewport().GetParent();

  public static PackedScene Pack(Node prototype)
  {
    var packed = new PackedScene();
    Error error = packed.Pack(prototype);
    prototype.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    return packed;
  }

  // Requires exactly one matching child.
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
