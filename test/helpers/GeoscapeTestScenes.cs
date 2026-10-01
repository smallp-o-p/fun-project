using System;
using System.Threading.Tasks;
using FunProject.GameState;
using FunProject.Geoscape;
using Godot;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Typed loaders for the authored geoscape scenes: the GdUnit runner's Godot instance uses
// the main project as its res:// root, so suites instantiate the real .tscn files and
// exercise their authored wiring (_Ready, %names, exports, button connections). The HUD
// suite packs small synthetic scenes locally for polymorphic/invalid-root contracts only.
// Cleanup stays with the callers and shared conventions: AddToTree marks the instance for
// AutoFree; layout/deletion waits and screen-space queries live here too.
internal static class GeoscapeTestScenes
{
  public static GeoscapeHud CreateHud()
    => GD.Load<PackedScene>("res://scenes/geoscape/GeoscapeHud.tscn").Instantiate<GeoscapeHud>();

  public static GeoscapeScene CreateGeoscapeScene(CampaignStartData start)
  {
    var scene = GD.Load<PackedScene>("res://scenes/geoscape/GeoscapeScene.tscn")
      .Instantiate<GeoscapeScene>();
    scene.Start = start;
    return scene;
  }

  public static UnitView CreateUnitView()
    => GD.Load<PackedScene>("res://scenes/geoscape/units/UnitView.tscn").Instantiate<UnitView>();

  public static SquadLoadoutView CreateSquadLoadoutView()
    => GD.Load<PackedScene>("res://scenes/geoscape/squad/SquadLoadoutView.tscn")
      .Instantiate<SquadLoadoutView>();

  public static CaptivityView CreateCaptivityView()
    => GD.Load<PackedScene>("res://scenes/geoscape/captivity/CaptivityView.tscn")
      .Instantiate<CaptivityView>();

  public static EngineeringView CreateEngineeringView()
    => GD.Load<PackedScene>("res://scenes/geoscape/engineering/EngineeringView.tscn")
      .Instantiate<EngineeringView>();

  public static GeoscapeEventResolution CreateResolutionView()
    => GD.Load<PackedScene>("res://scenes/geoscape/resolutions/GeoscapeEventResolution.tscn")
      .Instantiate<GeoscapeEventResolution>();

  public static DialogueView CreateDialogueView()
    => GD.Load<PackedScene>("res://scenes/dialogue/DialogueView.tscn").Instantiate<DialogueView>();

  // Base background shell for generic view-manager background/lifetime contracts.
  public static GeoscapeView CreateBaseView()
    => GD.Load<PackedScene>("res://scenes/geoscape/GeoscapeView.tscn").Instantiate<GeoscapeView>();

  // Checked synthetic packing: the prototype is freed and a non-Ok error fails loud
  // instead of returning a broken scene. Root types/names stay at the callers.
  public static PackedScene Pack(Node prototype)
  {
    var packed = new PackedScene();
    Error error = packed.Pack(prototype);
    prototype.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    return packed;
  }

  public static Control SquadSlot(SquadLoadoutView view, int slot)
    => view.GetNode<BoxContainer>("%Slots").GetChild<Control>(slot);

  public static Button SquadChoice(SquadLoadoutView view, string unitName)
    => view.GetNode<VBoxContainer>("%RosterChoices").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains(unitName));

  // Sequential by necessity: pressing Choose rebuilds the roster controls, so the choice
  // button must be reacquired AFTER that press, not captured beside it as an argument.
  public static void ChooseSquadUnit(SquadLoadoutView view, int slot, string unitName)
  {
    SquadSlot(view, slot).GetNode<Button>("%ChooseUnit")
      .EmitSignal(Button.SignalName.Pressed);
    SquadChoice(view, unitName).EmitSignal(Button.SignalName.Pressed);
  }

  public static Button ArmoryButton(UnitView editor, string itemName)
    => editor.GetNode<VBoxContainer>("%ArmoryList").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains(itemName));

  // Integration shell for manager suites: an authored root under a manager whose
  // ViewChanged presents every pushed/popped view. It never Configures, Pushes, Pops, or
  // clears selection itself; callers drive the stack and own its assertions.
  public static GeoscapeViewManager CreatePresentedManager(GeoscapeFixture fixture)
  {
    var manager = new GeoscapeViewManager { Name = "Manager" };
    var root = CreateBaseView();
    manager.RootView = root;
    manager.AddChild(root);
    AddToTree(manager);
    manager.ViewChanged += view => view.Present(fixture.State, fixture.Session);
    return manager;
  }

  public static Button SpeedButton(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Button>("%SpeedButton");

  // Region buttons share the marker's type; identify active markers by their authored
  // scene instead of counting region geometry or relying on generated node names.
  public static RegionButton[] MapEventMarkers(GeoscapeScene scene)
  {
    var map = scene.GetNode<GeoscapeMapControl>("%Map");
    return map.GetChildren().AsValueEnumerable().OfType<RegionButton>()
      .Where(button => button.SceneFilePath == map.EventMarkerScene!.ResourcePath).ToArray();
  }

  // Press the actual marker so its EventClicked signal reaches the composition root.
  public static void OpenResolutionViaMapEvent(GeoscapeScene scene, int eventIndex = 0)
    => MapEventMarkers(scene)[eventIndex].EmitSignal(BaseButton.SignalName.Pressed);

  public static Node3D AddBackdrop(GeoscapeView view, Node3D backdrop)
  {
    var background = view.GetNode<SubViewportContainer>("Background");
    background.Visible = true;
    background.GetNode<SubViewport>("Viewport").AddChild(backdrop);
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

  // The camera is code-built by GeoscapeScene under the map's own SubViewport.
  public static GeoscapeCameraRig MapCamera(GeoscapeScene scene)
    => scene.GetNode<GeoscapeMapControl>("%Map").GetViewport().GetNode<GeoscapeCameraRig>("Camera");

  public static SubViewportContainer MapViewport(GeoscapeScene scene)
    => (SubViewportContainer)scene.GetNode<GeoscapeMapControl>("%Map").GetViewport().GetParent();

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
